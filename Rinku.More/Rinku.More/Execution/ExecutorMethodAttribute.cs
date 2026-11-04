namespace Rinku;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ExecutorMethodAttribute(int priority = 0) : Attribute
{
    public int Priority { get; } = priority;
}
