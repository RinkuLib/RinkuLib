using System.Reflection;

namespace Rinku;

internal static partial class ExecutorCompiler
{
    internal readonly record struct ImplicitOperator(MethodInfo Method, Type Input, Type Output, bool Lifted);

    internal static IEnumerable<ExecutorCallCandidate> ConversionCandidates(Type source, Type target)
    {
        Type actualSource = Nullable.GetUnderlyingType(source) ?? source;
        Type actualTarget = Nullable.GetUnderlyingType(target) ?? target;
        foreach (Type type in actualSource == actualTarget ? new[] { actualSource } : new[] { actualSource, actualTarget })
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (method.Name == "op_Implicit" && !method.ContainsGenericParameters)
                {
                    yield return new(method);
                }
            }
        }
    }

    internal static ImplicitOperator? FindImplicitOperator(Type source, Type target)
    {
        if (HasStandardImplicitConversion(source, target) || source.IsInterface || target.IsInterface)
        {
            return null;
        }
        var candidates = new List<ImplicitOperator>();
        foreach (ExecutorCallCandidate candidate in ConversionCandidates(source, target))
        {
            MethodInfo method = (MethodInfo)candidate.Member;
            if (method.GetParameters() is not [{ ParameterType: var input }])
            {
                continue;
            }
            Type output = method.ReturnType;
            if (HasStandardImplicitConversion(source, input) && HasStandardImplicitConversion(output, target))
            {
                candidates.Add(new(method, input, output, false));
            }
            else if (input.IsValueType && output.IsValueType && !input.IsByRefLike && !output.IsByRefLike && Nullable.GetUnderlyingType(input) is null && Nullable.GetUnderlyingType(output) is null)
            {
                Type liftedInput = typeof(Nullable<>).MakeGenericType(input);
                Type liftedOutput = typeof(Nullable<>).MakeGenericType(output);
                if (HasStandardImplicitConversion(source, liftedInput) && HasStandardImplicitConversion(liftedOutput, target))
                {
                    candidates.Add(new(method, liftedInput, liftedOutput, true));
                }
            }
        }
        if (candidates.Count == 0)
        {
            return null;
        }
        Type? bestInput = null;
        Type? bestOutput = null;
        foreach (ImplicitOperator candidate in candidates)
        {
            bool inputWins = true;
            bool outputWins = true;
            foreach (ImplicitOperator other in candidates)
            {
                inputWins &= candidate.Input == source || other.Input != source && HasStandardImplicitConversion(candidate.Input, other.Input);
                outputWins &= candidate.Output == target || other.Output != target && HasStandardImplicitConversion(other.Output, candidate.Output);
            }
            if (inputWins)
            {
                bestInput = candidate.Input;
            }
            if (outputWins)
            {
                bestOutput = candidate.Output;
            }
        }
        ImplicitOperator? selected = null;
        foreach (ImplicitOperator candidate in candidates)
        {
            if (candidate.Input != bestInput || candidate.Output != bestOutput)
            {
                continue;
            }
            if (selected is { } previous && previous.Lifted == candidate.Lifted)
            {
                return null;
            }
            if (selected is null || !candidate.Lifted)
            {
                selected = candidate;
            }
        }
        return selected;
    }
}
