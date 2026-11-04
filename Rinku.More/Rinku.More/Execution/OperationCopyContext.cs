namespace Rinku;

public sealed class OperationCopyContext
{
    internal readonly ExecutorScope target;
    internal readonly Dictionary<PlanValue, OperationArgument> values;
    internal readonly Dictionary<PlanLocal, PlanLocal> locals;
    private readonly Func<ExecutorScope, ExecutorScope> scopes;

    internal OperationCopyContext(ExecutorScope target, Dictionary<PlanValue, OperationArgument> values, Dictionary<PlanLocal, PlanLocal> locals, Func<ExecutorScope, ExecutorScope> scopes)
    {
        this.target = target;
        this.values = values;
        this.locals = locals;
        this.scopes = scopes;
    }

    public Variable<T> Copy<T>(Variable<T> variable)
    {
        Variable result = Argument(OperationArgument.FromVariable(variable)).Variable ?? throw new InvalidOperationException("A fixed value cannot be copied as a variable.");
        return result.IsLocal ? new(target, result.Local) : new(target, result.Value);
    }

    public OperationArgument Argument(OperationArgument argument)
    {
        if (argument.IsValue)
        {
            return values[argument.Value];
        }
        if (argument.IsVariable)
        {
            return OperationArgument.FromLocal(locals[argument.Local]);
        }
        return argument;
    }

    internal PlanValue? Value(PlanValue? value) => value is not null && values.TryGetValue(value, out OperationArgument mapped) && mapped.IsValue ? mapped.Value : null;
    internal PlanLocal Local(PlanLocal local) => locals[local];
    internal PlanSlot Slot(PlanSlot slot) => slot.Local is not null ? locals[slot.Local].Slot : values[slot.Value!].IsValue ? values[slot.Value!].Value.Slot : values[slot.Value!].Local.Slot;
    public ExecutorScope Scope(ExecutorScope scope) => scopes(scope);
    public PlanSequence Sequence(PlanSequence source)
    {
        PlanSequence result = new();
        foreach (PlanInstruction instruction in source.Items)
        {
            result.Add(instruction.Copy(this));
        }
        return result;
    }
}
