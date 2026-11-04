using System.Reflection;

namespace Rinku;

public readonly struct ExecutorCallCandidate
{
    public readonly MemberInfo Member;
    public readonly bool Write;
    public readonly int Priority;
    internal readonly Type[] Inputs;
    internal readonly ParameterInfo[] Parameters;
    internal readonly int Offset;
    internal readonly Type ReturnType;

    public ExecutorCallCandidate(MemberInfo member, int priority = 0, bool write = false)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member is not (MethodBase { IsPublic: true } or FieldInfo { IsPublic: true }) || member is MethodInfo { IsStatic: true, IsAbstract: true })
        {
            throw new ArgumentException("A candidate must be a public callable method, constructor or field.", nameof(member));
        }
        if (write && member is not FieldInfo { IsInitOnly: false, IsLiteral: false })
        {
            throw new ArgumentException("Only writable fields can be assignment candidates.", nameof(member));
        }
        Member = member;
        Write = write;
        Priority = priority;
        Parameters = member is MethodBase method ? method.GetParameters() : [];
        bool instance = member is MethodInfo { IsStatic: false } or FieldInfo { IsStatic: false };
        Offset = instance ? 1 : 0;
        int count = member is FieldInfo ? (write ? 1 : 0) : Parameters.Length;
        Inputs = new Type[count + Offset];
        if (instance)
            Inputs[0] = member.DeclaringType!;
        for (int index = 0; index < count; index++)
        {
            Inputs[index + Offset] = member is FieldInfo field ? field.FieldType : Parameters[index].ParameterType;
        }
        ReturnType = member switch
        {
            MethodInfo function => function.ReturnType,
            ConstructorInfo constructor => constructor.DeclaringType!,
            FieldInfo field => write ? typeof(void) : field.FieldType,
            _ => throw new ArgumentException($"Member {member} is not callable.")
        };
    }

    internal bool SameTarget(ExecutorCallCandidate other) => Write == other.Write && (Member is MethodInfo method && other.Member is MethodInfo otherMethod ? ExecutorMethodCandidate.SameMethod(method, otherMethod) : Member.Equals(other.Member));

    internal static IEnumerable<ExecutorCallCandidate> Discover(IEnumerable<MemberInfo> members, int priority)
    {
        foreach (MemberInfo member in members)
        {
            if (member is MethodInfo method && (!method.IsStatic || !method.IsAbstract))
            {
                yield return new(method, checked(priority + (method.GetCustomAttribute<ExecutorMethodAttribute>()?.Priority ?? 0)));
            }
            else if (member is ConstructorInfo constructor)
            {
                yield return new(constructor, priority);
            }
            else if (member is FieldInfo field)
            {
                yield return new(field, priority);
                if (!field.IsInitOnly && !field.IsLiteral)
                {
                    yield return new(field, priority, true);
                }
            }
        }
    }
}
