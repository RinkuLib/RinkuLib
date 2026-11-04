using System.Reflection;

namespace Rinku;

internal sealed class ExecutorMethodPool
{
    private readonly Dictionary<Type, (int Priority, MemberInfo[]? Members)> types = [];
    private readonly Dictionary<string, List<ExecutorCallCandidate>> methods = new(StringComparer.Ordinal);

    internal ExecutorMethodPool(ExecutorBuildConfigurationSnapshot registrations)
    {
        foreach (var registration in registrations.Providers)
        {
            if (!types.TryGetValue(registration.Type, out var state) || registration.Priority > state.Priority)
                types[registration.Type] = (registration.Priority, null);
        }
        foreach (var registration in registrations.Candidates)
            AddMethod(registration.Method, registration.Priority);
        foreach (var registration in registrations.Providers)
            Include(registration.Type, registration.Priority);
    }

    private ExecutorMethodPool(ExecutorMethodPool source)
    {
        foreach (var type in source.types)
        {
            types.Add(type.Key, type.Value);
        }
        foreach (var group in source.methods)
        {
            methods.Add(group.Key, new(group.Value));
        }
    }

    internal ExecutorMethodPool Clone() => new(this);

    internal void Include(Type type) => Include(type, type.GetCustomAttribute<ExecutorProviderAttribute>()?.Priority ?? 0);

    private void Include(Type type, int priority)
    {
        bool seen = types.TryGetValue(type, out var state);
        if (state.Members is not null && priority <= state.Priority)
            return;
        if (seen)
            priority = Math.Max(priority, state.Priority);
        MemberInfo[] discovered = state.Members ?? type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        types[type] = (priority, discovered);
        foreach (ExecutorCallCandidate candidate in ExecutorCallCandidate.Discover(discovered, priority))
        {
            if (candidate.Member is not ConstructorInfo)
            {
                AddMember(candidate);
            }
        }
        foreach (Type parent in type.GetInterfaces())
        {
            Include(parent, priority);
        }
    }

    internal IReadOnlyList<ExecutorCallCandidate> Find(string name) => methods.TryGetValue(name, out var candidates) ? candidates : [];

    internal int Priority(Type type) => types.TryGetValue(type, out var state) ? state.Priority : type.GetCustomAttribute<ExecutorProviderAttribute>()?.Priority ?? 0;

    internal IEnumerable<MemberInfo> Members(Type type) => types.TryGetValue(type, out var state) && state.Members is not null ? state.Members : type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

    internal int Priority(ExecutorCallCandidate candidate)
    {
        int priority = candidate.Priority;
        foreach (ExecutorCallCandidate registered in Find(candidate.Member.Name))
        {
            if (registered.SameTarget(candidate))
            {
                priority = Math.Max(priority, registered.Priority);
            }
        }
        return priority;
    }

    private void AddMethod(MethodInfo method, int priority)
    {
        AddMember(new(method, priority));
    }

    private void AddMember(ExecutorCallCandidate candidate)
    {
        Add(candidate.Member.Name, candidate);
        if (candidate.Member is MethodInfo { IsSpecialName: true } method && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal)))
        {
            Add(method.Name[4..], candidate);
        }
    }

    private void Add(string name, ExecutorCallCandidate candidate)
    {
        if (!methods.TryGetValue(name, out var candidates))
            methods.Add(name, candidates = []);
        for (int index = 0; index < candidates.Count; index++)
        {
            if (!candidates[index].SameTarget(candidate))
                continue;
            if (candidate.Priority > candidates[index].Priority)
                candidates[index] = candidate;
            return;
        }
        candidates.Add(candidate);
    }
}
