using System.Reflection;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed partial class OperationBindingContext
{
    internal ExecutorCallCandidate BindCall(IEnumerable<ExecutorCallCandidate> candidates, OperationArgument[] operands, IReadOnlyList<ExecutorArgumentPassing> passing, Type[] genericArguments, Type? resultType, string description)
    {
        OperationArgument[] arguments = operands;
        List<(ExecutorCallCandidate Call, int[] Score)> best = [];
        bool nullRejected = false;
        foreach (ExecutorCallCandidate registration in candidates)
        {
            ExecutorCallCandidate candidate = registration;
            if (candidate.Member is MethodInfo function && (function.ContainsGenericParameters || genericArguments.Length != 0))
            {
                MethodInfo? closed = CloseCandidate(function, arguments, resultType ?? typeof(void), genericArguments);
                if (closed is null)
                    continue;
                candidate = new(closed, candidate.Priority);
            }
            if (resultType is not null && candidate.ReturnType == typeof(void))
                continue;
            int[]? score = ParameterScore(candidate, arguments, passing);
            if (score is null)
            {
                nullRejected |= ParameterScore(candidate, arguments, passing, false) is not null;
                continue;
            }
            int duplicate = best.FindIndex(item => item.Call.SameTarget(candidate));
            if (duplicate >= 0)
            {
                if (candidate.Priority > best[duplicate].Call.Priority)
                    best[duplicate] = (candidate, score);
                continue;
            }
            if (best.Any(item => IsMoreSpecific(item.Call, item.Score, candidate, score, genericArguments.Length != 0)))
                continue;
            best.RemoveAll(item => IsMoreSpecific(candidate, score, item.Call, item.Score, genericArguments.Length != 0));
            best.Add((candidate, score));
        }
        if (best.Count > 1)
        {
            int priority = best.Max(item => item.Call.Priority);
            best.RemoveAll(item => item.Call.Priority < priority);
        }
        if (best.Count > 1)
            throw new InvalidOperationException($"More than one candidate binds {description} at priority {best[0].Call.Priority}: {string.Join(", ", best.Select(item => $"{item.Call.Member.DeclaringType}.{item.Call.Member}"))}.");
        if (best.Count == 0)
            throw new ExecutorBindingException(nullRejected ? ExecutorBindingFailure.NullNotAccepted : ExecutorBindingFailure.MissingImplementation, $"No candidate implements {description} for ({string.Join(", ", arguments.Select(GetArgumentType))}).");
        ExecutorCallCandidate selected = best[0].Call;
        if (selected.Member is MethodInfo { IsVirtual: true } method && method.DeclaringType?.IsVisible == false && method.GetBaseDefinition().DeclaringType?.IsVisible == true)
            return new(method.GetBaseDefinition(), selected.Priority);
        return selected;
    }

    internal MethodInfo? CloseCandidate(MethodInfo candidate, IReadOnlyList<OperationArgument> arguments, Type expectedResult, Type[] hints)
    {
        if (hints.Length != 0 && candidate.IsGenericMethod && candidate.GetGenericArguments().Length != hints.Length)
            return null;
        if (!candidate.ContainsGenericParameters)
            return hints.Length == 0 || !candidate.IsGenericMethod || candidate.GetGenericArguments().SequenceEqual(hints) ? candidate : null;
        if (!candidate.IsGenericMethodDefinition)
            return null;
        try
        {
            Type[] genericArguments = InferMethodArguments(candidate, arguments, expectedResult, hints);
            return candidate.MakeGenericMethod(genericArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal Type[] InferMethodArguments(MethodInfo definition, IReadOnlyList<OperationArgument> arguments, Type expectedResult, Type[] hints)
    {
        Type[] genericParameters = definition.GetGenericArguments();
        Dictionary<Type, Type?> inferred = [];
        ParameterInfo[] parameters = definition.GetParameters();
        int offset = definition.IsStatic ? 0 : 1;
        if (parameters.Length + offset != arguments.Count)
            throw new InvalidOperationException($"Method {definition} has {parameters.Length} parameters but the plan supplies {arguments.Count}.");
        for (int index = 0; index < parameters.Length; index++)
        {
            if (!parameters[index].IsOut)
                InferGenericArguments(parameters[index].ParameterType, GetArgumentType(arguments[index + offset]), inferred);
        }
        for (int index = 0; index < parameters.Length; index++)
        {
            if (!parameters[index].IsOut)
                continue;
            Dictionary<Type, Type?> outputHints = [];
            InferGenericArguments(parameters[index].ParameterType, GetArgumentType(arguments[index + offset]), outputHints);
            foreach (var hint in outputHints)
            {
                inferred.TryAdd(hint.Key, hint.Value);
            }
        }

        if (definition.ReturnType.ContainsGenericParameters && expectedResult != typeof(void) && !expectedResult.ContainsGenericParameters)
        {
            Dictionary<Type, Type?> resultArguments = [];
            InferGenericArguments(definition.ReturnType, expectedResult, resultArguments);
            if (expectedResult.IsGenericType)
            {
                Type resultDefinition = expectedResult.GetGenericTypeDefinition();
                if (TryGetGenericShape(definition.ReturnType, resultDefinition, out Type? shape) && shape is not null)
                    InferGenericArguments(shape, expectedResult, resultArguments);
            }
            foreach (var (parameter, type) in resultArguments)
                inferred.TryAdd(parameter, type);
        }

        if (hints.Length == genericParameters.Length)
        {
            for (int index = 0; index < hints.Length; index++)
            {
                inferred.TryAdd(genericParameters[index], hints[index]);
            }
        }
        Type[] result = new Type[genericParameters.Length];
        for (int i = 0; i < genericParameters.Length; i++)
        {
            if (!inferred.TryGetValue(genericParameters[i], out Type? type) || type is null)
                throw new InvalidOperationException($"The generic argument {genericParameters[i].Name} of {definition} could not be uniquely inferred from its inputs and result.");
            result[i] = type;
        }
        return result;
    }

    internal int[]? ParameterScore(ExecutorCallCandidate candidate, IReadOnlyList<OperationArgument> arguments, IReadOnlyList<ExecutorArgumentPassing> passing, bool checkNulls = true)
    {
        ParameterInfo[] parameters = candidate.Parameters;
        int offset = candidate.Offset;
        if (candidate.Inputs.Length != arguments.Count)
            return null;
        int[] score = new int[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            Type sourceType = GetArgumentType(arguments[i]);
            Type targetType = candidate.Inputs[i];
            ExecutorArgumentPassing expectedPassing = i < offset || !targetType.IsByRef ? ExecutorArgumentPassing.Value : parameters[i - offset].IsOut ? ExecutorArgumentPassing.Out : parameters[i - offset].IsIn ? ExecutorArgumentPassing.In : ExecutorArgumentPassing.Ref;
            if (passing[i] != expectedPassing)
                return null;
            Availability state = State(arguments[i]);
            bool output = targetType.IsByRef && i >= offset && parameters[i - offset].IsOut;
            if (!output && state == Availability.Unavailable || checkNulls && (i < offset && state == Availability.KnownNull && !targetType.IsValueType || !output && i >= offset && state == Availability.KnownNull && !(candidate.Member is FieldInfo field ? AcceptsNull(field) : AcceptsNull(parameters[i - offset]))))
                return null;
            if (state == Availability.KnownNull && arguments[i].Kind == OperationArgumentKind.Constant)
                continue;
            if (targetType.IsByRef)
            {
                Type element = targetType.GetElementType()!;
                PlanSlot? slot = arguments[i].IsValue ? arguments[i].Value.Slot : arguments[i].IsVariable ? arguments[i].Local.Slot : null;
                if (slot is null)
                    return null;
                bool unbound = (!variables.TryGetValue(slot, out VariableBinding binding) || !binding.Bound);
                if (output && unbound)
                {
                    score[i] = 0;
                    continue;
                }
                if (sourceType != element)
                    return null;
                continue;
            }
            if (sourceType == targetType)
                continue;
            if (HasImplicitConversion(sourceType, targetType))
            {
                score[i] = 1;
                continue;
            }
            if (IsNullableCompatible(targetType, sourceType))
            {
                score[i] = 2;
                continue;
            }
            if (IsNumeric(sourceType) && IsNumeric(targetType))
            {
                score[i] = 2;
                continue;
            }
            return null;
        }
        return score;
    }

    internal bool IsStructuralConcrete(Type logicalType, Type actualType)
    {
        if (IsExpressionContract(logicalType))
            return true;
        if (logicalType.IsArray && actualType.IsArray && logicalType.GetArrayRank() == actualType.GetArrayRank())
            return IsStructuralConcrete(logicalType.GetElementType() ?? logicalType, actualType.GetElementType() ?? actualType);
        if (!logicalType.IsGenericType || !TryGetGenericShape(actualType, logicalType.GetGenericTypeDefinition(), out Type? actualShape) || actualShape is null)
            return false;

        Type[] logicalArguments = logicalType.GetGenericArguments();
        Type[] actualArguments = actualShape.GetGenericArguments();
        return logicalArguments.Length == actualArguments.Length && logicalArguments.Zip(actualArguments).All(x => x.First == x.Second || x.First.IsAssignableFrom(x.Second) || IsStructuralConcrete(x.First, x.Second));
    }

    internal bool RequiresStructuralBinding(Type logicalType, Type actualType) => !logicalType.IsAssignableFrom(actualType) && ContainsExpressionContract(logicalType) && IsStructuralConcrete(logicalType, actualType);

    internal Type SpecializeType(Type logicalType)
    {
        if (inputTypeBindings.TryGetValue(logicalType, out Type? specialized))
            return specialized ?? logicalType;
        if (logicalType.IsArray)
        {
            Type? logicalElementType = logicalType.GetElementType();
            if (logicalElementType is null)
                return logicalType;
            Type specializedElementType = SpecializeType(logicalElementType);
            if (specializedElementType == logicalElementType)
                return logicalType;
            Type specializedArrayType = specializedElementType.MakeArrayType(logicalType.GetArrayRank());
            return logicalType.IsAssignableFrom(specializedArrayType) || ContainsExpressionContract(logicalType) && IsStructuralConcrete(logicalType, specializedArrayType) ? specializedArrayType : logicalType;
        }
        if (!logicalType.IsGenericType)
            return logicalType;
        Type[] arguments = logicalType.GetGenericArguments();
        Type[] specializedArguments = arguments.Select(SpecializeType).ToArray();
        if (arguments.SequenceEqual(specializedArguments))
            return logicalType;
        try
        {
            Type specializedType = logicalType.GetGenericTypeDefinition().MakeGenericType(specializedArguments);
            return logicalType.IsAssignableFrom(specializedType) || ContainsExpressionContract(logicalType) && IsStructuralConcrete(logicalType, specializedType) ? specializedType : logicalType;
        }
        catch (ArgumentException)
        {
            return logicalType;
        }
    }

    internal void BindInputType(Type logicalType, Type actualType)
    {
        methodPool.Include(actualType);
        if (inputTypeBindings.TryGetValue(logicalType, out Type? previous))
            inputTypeBindings[logicalType] = previous == actualType ? previous : null;
        else
            inputTypeBindings.Add(logicalType, actualType);
        if (logicalType.IsArray && actualType.IsArray && logicalType.GetArrayRank() == actualType.GetArrayRank())
            BindInputType(logicalType.GetElementType()!, actualType.GetElementType()!);

        if (logicalType.IsGenericType && actualType.IsGenericType)
        {
            Type logicalDefinition = logicalType.GetGenericTypeDefinition();
            if (TryGetGenericShape(actualType, logicalDefinition, out Type? actualShape) && actualShape is not null)
            {
                Type[] logicalArguments = logicalType.GetGenericArguments();
                Type[] actualArguments = actualShape.GetGenericArguments();
                for (int i = 0; i < logicalArguments.Length; i++)
                    BindInputType(logicalArguments[i], actualArguments[i]);
            }
        }
    }
}
