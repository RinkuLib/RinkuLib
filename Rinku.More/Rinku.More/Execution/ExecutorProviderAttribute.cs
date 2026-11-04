namespace Rinku;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ExecutorProviderAttribute(int priority = 0) : Attribute
{
    public int Priority { get; } = priority;
}
