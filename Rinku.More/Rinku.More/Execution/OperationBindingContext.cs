using System.Reflection;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    public PlanSequence Resolve(PlanSequence sequence) => BindSequence(sequence);
    public void RequireArguments(IEnumerable<OperationArgument> arguments)
    {
        foreach (OperationArgument argument in arguments)
        {
            Require(argument);
        }
    }
    public Type TypeOf(Variable variable) => GetSlotType(variable.Slot);
    public Type TypeOf(OperationArgument argument) => GetArgumentType(ResolveReferenceOperand(argument));
    public void Require(OperationArgument argument) => EnsureAvailable(argument);
    public void Define(Variable variable, Type type, bool knownNull = false) => Define(variable.Slot, type, knownNull ? Availability.KnownNull : Availability.Available);
    public IEnumerable<ExecutorCallCandidate> Find(string name) => methodPool.Find(name);
    public IEnumerable<ExecutorCallCandidate> Find(Type type)
    {
        foreach (ExecutorCallCandidate candidate in ExecutorCallCandidate.Discover(methodPool.Members(type), methodPool.Priority(type)))
        {
            int priority = methodPool.Priority(candidate);
            yield return priority == candidate.Priority ? candidate : new(candidate.Member, priority, candidate.Write);
        }
    }

    public IPlanNode? Call(string name, IReadOnlyList<OperationArgument> arguments, Variable? output = null) => Call([name], arguments, output);
    public IPlanNode? Call(IReadOnlyList<string> names, IReadOnlyList<OperationArgument> arguments, Variable? output = null)
    {
        PlanValue? result = output is null ? null : output.IsLocal ? plan.CreateValue(output.LogicalType) : output.Value;
        CallOperation call = new(names, arguments.ToArray(), result, new ExecutorArgumentPassing[arguments.Count], []);
        return AssignOutput(call.Resolve(this), result, output);
    }

    public IPlanNode? Bind(ExecutorCallCandidate candidate, IReadOnlyList<OperationArgument> arguments, Variable? output = null)
    {
        OperationArgument[] actual = ResolveReferenceOperands(arguments);
        ParameterInfo[] parameters = candidate.Parameters;
        ExecutorArgumentPassing[] passing = new ExecutorArgumentPassing[actual.Length];
        for (int index = candidate.Offset; index < actual.Length && index - candidate.Offset < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index - candidate.Offset];
            passing[index] = !parameter.ParameterType.IsByRef ? ExecutorArgumentPassing.Value : parameter.IsOut ? ExecutorArgumentPassing.Out : parameter.IsIn ? ExecutorArgumentPassing.In : ExecutorArgumentPassing.Ref;
        }
        for (int index = 0; index < actual.Length; index++)
        {
            if (passing[index] != ExecutorArgumentPassing.Out)
            {
                EnsureAvailable(actual[index]);
            }
        }
        ExecutorCallCandidate selected = BindCall([candidate], actual, passing, [], output?.LogicalType, candidate.Member.Name);
        PlanValue? result = output is null ? null : output.IsLocal ? plan.CreateValue(output.LogicalType) : output.Value;
        return AssignOutput(BindSelectedMember(selected, actual, result), result, output);
    }

    private IPlanNode? AssignOutput(IPlanNode? operation, PlanValue? result, Variable? output)
    {
        if (output?.IsLocal != true)
        {
            return operation;
        }
        PlanSequence sequence = new();
        if (operation is not null)
        {
            sequence.Add(operation);
        }
        StoreOperation store = new(OperationArgument.FromLocal(output.Local), OperationArgument.FromValue(result!));
        sequence.Add(store.Resolve(this)!);
        return new ScopeNode(sequence);
    }

    internal IPlanNode? BindSelectedMember(ExecutorCallCandidate selected, OperationArgument[] arguments, PlanValue? result)
    {
        if (selected.Member is MethodInfo factory && typeof(ExecutorScope).IsAssignableFrom(factory.ReturnType))
        {
            if (!preparing.Add(factory))
                throw new InvalidOperationException($"Executor scope preparation recursively invokes {factory}.");
            try
            {
                ExecutorScope scope = Prepare(factory, arguments);
                scopes.Push([]);
                try {
                    ParameterInfo[] parameters = factory.GetParameters();
                    int offset = factory.IsStatic ? 0 : 1;
                    for (int index = 0; index < arguments.Length; index++) {
                        OperationArgument argument = arguments[index];
                        string? name = index < offset ? null : parameters[index - offset].Name;
                        PlanValue value = plan.CreateVariable(GetArgumentType(argument), name);
                        references[value.Slot] = argument;
                        Publish(value.Slot);
                    }
                    return new CompositionNode(scope, result).Resolve(this);
                }
                finally {
                    scopes.Pop();
                    if (result is not null)
                        Publish(result.Slot);
                }
            }
            finally
            {
                preparing.Remove(factory);
            }
        }
        if (selected.Member is MethodInfo { ReturnType.IsByRef: true })
            throw new NotSupportedException($"By-reference return type on {selected.Member} is not supported by executors.");
        if (selected.Member.DeclaringType is { IsVisible: false } declaring) AccessTypes.Add(declaring);
        Define(result, selected.ReturnType);
        if (selected.Member is FieldInfo field)
        {
            return selected.Write ? new BoundFieldWriteOperation(field, arguments) : new BoundFieldReadOperation(field, arguments, result);
        }
        MethodBase method = (MethodBase)selected.Member;
        if (method is MethodInfo { IsGenericMethod: true } generic)
        {
            foreach (Type argument in generic.GetGenericArguments())
            {
                ValidatePlanType(argument, "selected generic argument");
            }
        }
        ValidateParameters(method, arguments);
        ApplyParameterContracts(method, arguments, result);
        return new BoundCallOperation(method, arguments, result);
    }
}

