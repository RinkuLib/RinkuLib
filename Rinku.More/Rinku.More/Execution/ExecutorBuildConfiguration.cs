using System.Reflection;

namespace Rinku;

public sealed class ExecutorBuildConfiguration
{
    public static ExecutorBuildConfiguration Global { get; } = new();
    private readonly object sync = new();
    private readonly HashSet<Type> executorInterfaces = [];
    private readonly List<ExecutorProviderRegistration> providers = [];
    private readonly List<ExecutorMethodCandidate> candidates = [];
    private readonly HashSet<Assembly> assemblies = [];

    public ExecutorBuildConfiguration Copy()
    {
        lock (sync)
        {
            var copy = new ExecutorBuildConfiguration();
            copy.executorInterfaces.UnionWith(executorInterfaces);
            copy.providers.AddRange(providers);
            copy.candidates.AddRange(candidates);
            copy.assemblies.UnionWith(assemblies);
            return copy;
        }
    }

    /// <summary>Requests an executor interface whose input types define a compiled execution signature.</summary>
    public ExecutorBuildConfiguration Support<TExecutor>() where TExecutor : IExecutor => Support(typeof(TExecutor));

    /// <summary>Adds one or more executor signatures declared by an interface.</summary>
    public ExecutorBuildConfiguration Support(Type executorInterface)
    {
        ExecutorType.GetSignatures(executorInterface);
        lock (sync)
        {
            executorInterfaces.Add(executorInterface);
        }
        return this;
    }

    public ExecutorBuildConfiguration RegisterProvider<TProvider>(int priority = 0) => RegisterProvider(typeof(TProvider), priority);

    public ExecutorBuildConfiguration RegisterProvider(Type providerType, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        if (providerType.ContainsGenericParameters)
            throw new ArgumentException("A provider type must be closed before registration.", nameof(providerType));
        int providerPriority = checked(priority + (providerType.GetCustomAttribute<ExecutorProviderAttribute>()?.Priority ?? 0));
        lock (sync)
        {
            int index = providers.FindIndex(item => item.Type == providerType);
            if (index < 0)
                providers.Add(new(providerType, providerPriority));
            else if (providerPriority > providers[index].Priority)
                providers[index] = new(providerType, providerPriority);
        }
        return this;
    }

    public ExecutorBuildConfiguration RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (sync)
        {
            if (assemblies.Contains(assembly))
                return this;
            var staged = new ExecutorBuildConfiguration();
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsDefined(typeof(ExecutorProviderAttribute), false))
                {
                    staged.RegisterProvider(type);
                    continue;
                }
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.IsDefined(typeof(ExecutorMethodAttribute), false))
                        staged.RegisterMethod(method);
                }
            }
            ImportProviders(staged.Capture());
            assemblies.Add(assembly);
        }
        return this;
    }

    private void ImportProviders(ExecutorBuildConfigurationSnapshot registrations)
    {
        lock (sync)
        {
            foreach (var registration in registrations.Providers)
            {
                int index = providers.FindIndex(item => item.Type == registration.Type);
                if (index < 0)
                    providers.Add(registration);
                else if (registration.Priority > providers[index].Priority)
                    providers[index] = registration;
            }
            foreach (var registration in registrations.Candidates)
            {
                int index = candidates.FindIndex(item => ExecutorMethodCandidate.SameMethod(item.Method, registration.Method));
                if (index < 0)
                    candidates.Add(registration);
                else if (registration.Priority > candidates[index].Priority)
                    candidates[index] = registration;
            }
        }
    }

    public ExecutorBuildConfiguration RegisterMethod(MethodInfo method, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.IsPublic || method.IsStatic && method.IsAbstract)
            throw new ArgumentException($"Candidate method {method} must be public and callable.", nameof(method));
        int effectivePriority = checked(priority + (method.GetCustomAttribute<ExecutorMethodAttribute>()?.Priority ?? 0));
        lock (sync)
        {
            int index = candidates.FindIndex(item => ExecutorMethodCandidate.SameMethod(item.Method, method));
            if (index < 0)
                candidates.Add(new(method, effectivePriority));
            else if (effectivePriority > candidates[index].Priority)
                candidates[index] = new(method, effectivePriority);
        }
        return this;
    }

    private ExecutorBuildConfigurationSnapshot Capture()
    {
        lock (sync)
        {
            return new(providers.ToArray(), candidates.ToArray(), executorInterfaces.ToArray());
        }
    }

    internal ExecutorBuildConfigurationSnapshot Snapshot()
    {
        var global = Global.Capture();
        if (ReferenceEquals(this, Global))
            return global;
        var local = Capture();
        return new([.. global.Providers.Union(local.Providers)], [.. global.Candidates.Union(local.Candidates)], [.. global.ExecutorInterfaces.Union(local.ExecutorInterfaces)]);
    }

    /// <summary>Clears registered candidates and requested executor interfaces.</summary>
    public ExecutorBuildConfiguration Clear()
    {
        lock (sync)
        {
            providers.Clear();
            candidates.Clear();
            assemblies.Clear();
            executorInterfaces.Clear();
        }
        return this;
    }


}

internal readonly record struct ExecutorProviderRegistration(Type Type, int Priority);
internal readonly record struct ExecutorMethodCandidate(MethodInfo Method, int Priority)
{
    internal static bool SameMethod(MethodInfo left, MethodInfo right) => left.DeclaringType == right.DeclaringType && left.HasSameMetadataDefinitionAs(right) && left.GetGenericArguments().SequenceEqual(right.GetGenericArguments());
}

internal sealed class ExecutorBuildConfigurationSnapshot(ExecutorProviderRegistration[] providers, ExecutorMethodCandidate[] candidates, Type[] executorInterfaces)
{
    internal IReadOnlyList<Type> ExecutorInterfaces { get; } = executorInterfaces;
    internal IReadOnlyList<ExecutorProviderRegistration> Providers { get; } = providers;
    internal IReadOnlyList<ExecutorMethodCandidate> Candidates { get; } = candidates;
}
