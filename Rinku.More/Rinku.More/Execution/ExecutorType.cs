using System.Reflection;

namespace Rinku;

internal static class ExecutorType
{
    internal static IReadOnlyList<(MethodInfo Method, Type[] Inputs)> GetSignatures(Type executorType)
    {
        if (!executorType.IsInterface || executorType.ContainsGenericParameters)
            throw new ArgumentException($"{executorType} must be a closed executor interface.", nameof(executorType));
        List<(MethodInfo Method, Type[] Inputs)> result = [];
        foreach (Type contract in executorType.GetInterfaces().Prepend(executorType))
        {
            foreach (MethodInfo method in contract.GetMethods())
            {
                if (!method.IsAbstract)
                    continue;
                if (method.IsStatic || method.IsGenericMethod || method.ReturnType.IsByRef || method.ReturnType.IsByRefLike || method.ReturnType.IsPointer || method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef || parameter.ParameterType.IsByRefLike || parameter.ParameterType.IsPointer))
                    throw new ArgumentException($"{executorType} has an unsupported abstract member {method}.", nameof(executorType));
                result.Add((method, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray()));
            }
        }
        if (result.Count == 0)
            throw new ArgumentException($"{executorType} declares no executor signature.", nameof(executorType));
        return result;
    }
}
