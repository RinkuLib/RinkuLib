using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    internal readonly Stack<List<PlanSlot>> scopes = new([[]]);
    internal Dictionary<PlanSlot, OperationArgument> constants = [];
    internal readonly Dictionary<PlanSlot, OperationArgument> references = [];
    internal readonly HashSet<MethodInfo> preparing = [];
    internal readonly HashSet<ExecutorScope> composing = [];

    internal IReadOnlyList<MethodInfo> returnSignatures = [];
    internal OperationArgument Result => ResolveReferenceOperand(plan.Output is null ? default : OperationArgument.FromVariable(plan.Output));

    internal PlanSequence BindPlan(PlanSequence instructions, IReadOnlyList<MethodInfo> signatures)
    {
        returnSignatures = signatures;
        PlanSequence body = BindSequence(instructions);
        if (reachable)
        {
            ValidateResults();
        }
        return body;
    }

    internal void ValidateResults()
    {
        foreach (MethodInfo signature in returnSignatures)
        {
            ValidateResult(signature);
        }
    }

    internal void ValidateResult(MethodInfo signature)
    {
        if (signature.ReturnType == typeof(void))
            return;
        if (plan.Output is null)
            throw new ExecutorBindingException(ExecutorBindingFailure.UnavailableInput, "A value-returning executor must expose a result.");
        OperationArgument output = Result;
        EnsureAvailable(output);
        Type actual = GetArgumentType(output);
        if (State(output) == Availability.KnownNull && Nullable.GetUnderlyingType(signature.ReturnType) is null && (signature.ReturnType.IsValueType || nullability.Create(signature.ReturnParameter).ReadState != NullabilityState.Nullable))
            throw new ExecutorBindingException(ExecutorBindingFailure.NullNotAccepted, $"The result of {signature} does not accept a known null value.");
        if (!HasImplicitConversion(actual, signature.ReturnType))
            throw new ExecutorBindingException(ExecutorBindingFailure.TypeMismatch, $"The executor result {actual} cannot be returned as {signature.ReturnType}.");
    }

    internal void Publish(PlanSlot slot)
    {
        if (!slot.IsPublished && !slot.IsInput)
            return;
        List<PlanSlot> scope = scopes.Peek();
        foreach (PlanSlot existing in scope)
        {
            if (ReferenceEquals(existing, slot))
                return;
            if (slot.Name is not null && string.Equals(existing.Name, slot.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"The current scope already declares '{slot.Name}'.");
        }
        scope.Add(slot);
    }

    internal OperationArgument ResolveReference(PlanSlot reference)
    {
        foreach (List<PlanSlot> scope in scopes)
        {
            PlanSlot? match = null;
            int best = int.MaxValue;
            bool ambiguous = false;
            foreach (PlanSlot candidate in scope)
            {
                if (reference.Name is not null && !string.Equals(reference.Name, candidate.Name, StringComparison.Ordinal))
                    continue;
                Type actual = GetSlotType(candidate);
                int rank = actual == reference.LogicalType ? 0 : reference.LogicalType.IsAssignableFrom(actual) ? 1 : candidate.LogicalType == reference.LogicalType ? 2 : int.MaxValue;
                if (rank == int.MaxValue)
                {
                    if (reference.Name is not null)
                        throw new ExecutorBindingException(ExecutorBindingFailure.TypeMismatch, $"Enclosing value '{reference.Name}' has type {actual}, not {reference.LogicalType}.");
                    continue;
                }
                if (rank > best)
                    continue;
                if (rank == best)
                {
                    ambiguous = true;
                    continue;
                }
                match = candidate;
                best = rank;
                ambiguous = false;
            }
            if (ambiguous)
                throw new InvalidOperationException($"More than one enclosing value matches {reference.LogicalType}; give the requested variable a name.");
            if (match is not null)
                return match.Value is PlanValue value ? OperationArgument.FromValue(value) : OperationArgument.FromLocal(match.Local!);
        }
        if (reference.DefaultWhenMissing)
            return OperationArgument.FromTypedConstant(reference.LogicalType, DefaultValue(reference.LogicalType));
        throw new ExecutorBindingException(ExecutorBindingFailure.UnavailableInput, $"No enclosing scope provides {reference.Name ?? reference.LogicalType.Name}.");
    }

    internal ExecutorScope Prepare(MethodInfo factory, OperationArgument[] operands)
    {
        int offset = factory.IsStatic ? 0 : 1;
        object? instanceValue = null;
        if (offset != 0 && (!TryGetConstant(operands[0], out OperationArgument instance) || (instanceValue = instance.Data) is null))
            throw new InvalidOperationException($"Preparation {factory} requires a fixed instance available during compilation.");
        ParameterInfo[] parameters = factory.GetParameters();
        object?[] values = new object?[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
        {
            Type type = parameters[index].ParameterType;
            if (type.IsByRef)
                type = type.GetElementType()!;
            values[index] = !parameters[index].IsOut && TryGetConstant(operands[index + offset], out OperationArgument value) ? value.Data : DefaultValue(type);
        }
        try
        {
            ExecutorScope scope = (ExecutorScope?)factory.Invoke(instanceValue, values) ?? throw new InvalidOperationException($"Preparation {factory} returned null.");
            return scope;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    internal bool TryGetConstant(OperationArgument operand, out OperationArgument value)
    {
        if (operand.Variable is Variable variable)
        {
            value = default;
            return loopBindings == 0 && constants.TryGetValue(variable.Slot, out value);
        }
        value = operand;
        return true;
    }

    internal static object? DefaultValue(Type type) => type.IsValueType ? Array.CreateInstance(type, 1).GetValue(0) : null;

    internal OperationArgument ResolveReferenceOperand(OperationArgument operand)
    {
        while (operand.Variable is Variable variable && references.TryGetValue(variable.Slot, out OperationArgument reference))
        {
            operand = reference;
        }
        return operand;
    }

    internal OperationArgument[] ResolveReferenceOperands(IReadOnlyList<OperationArgument> operands)
    {
        OperationArgument[] result = new OperationArgument[operands.Count];
        for (int index = 0; index < operands.Count; index++)
        {
            result[index] = ResolveReferenceOperand(operands[index]);
        }
        return result;
    }
    internal IPlanNode BindScope(ExecutorScope source, PlanValue? result = null)
    {
        if (!composing.Add(source))
            throw new InvalidOperationException("Executor scopes cannot recursively compose themselves.");
        try
        {

            OperationArgument[] inputs = new OperationArgument[source.Inputs.Count];
            for (int index = 0; index < inputs.Length; index++)
            {
                inputs[index] = ResolveReferenceOperand(ResolveReference(source.Inputs[index].Slot));
            }
            PlanSequence imported = new();
            Variable? outputVariable = ImportGraph(plan, source, inputs, imported);
            OperationArgument output = outputVariable is null ? default : OperationArgument.FromVariable(outputVariable);
            Dictionary<Type, Type?> outerTypes = inputTypeBindings;
            inputTypeBindings = [];
            ExecutorMethodPool outerPool = methodPool;
            methodPool = methodPool.Clone();
            for (int index = 0; index < source.Inputs.Count; index++)
            {
                BindInputType(source.Inputs[index].LogicalType, GetArgumentType(inputs[index]));
            }
            foreach (var outerType in outerTypes)
            {
                inputTypeBindings.TryAdd(outerType.Key, outerType.Value);
            }
            scopes.Push([]);
            PlanSequence body;
            try
            {
                body = BindSequence(imported);
                output = ResolveReferenceOperand(output);
                if (reachable && result is not null)
                {
                    if (outputVariable is null)
                        throw new ExecutorBindingException(ExecutorBindingFailure.UnavailableInput, "A composed scope must expose a result when its caller requests one.");
                    EnsureAvailable(output);
                }
            }
            finally
            {
                scopes.Pop();
                methodPool = outerPool;
                inputTypeBindings = outerTypes;
            }
            if (reachable && result is not null)
            {
                Define(result, GetArgumentType(output!), State(output!));
                body.Add(new StoreOperation(OperationArgument.FromValue(result), output));
                if (TryGetConstant(output, out OperationArgument constant))
                    constants[result.Slot] = constant;
            }
            return new ScopeNode(body);
        }
        finally
        {
            composing.Remove(source);
        }
    }
}

