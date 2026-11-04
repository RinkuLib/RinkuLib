namespace Rinku;

/// <summary>Identifies one value slot in an executor plan.</summary>
public abstract class Variable
{
    internal Variable(ExecutorScope plan, PlanValue value)
    {
        Plan = plan;
        Slot = value.Slot;
        LogicalType = value.LogicalType;
    }

    internal Variable(ExecutorScope plan, PlanLocal local)
    {
        Plan = plan;
        Slot = local.Slot;
        LogicalType = local.LogicalType;
    }

    internal ExecutorScope Plan
    {
        get;
    }
    internal PlanSlot Slot
    {
        get;
    }
    internal PlanValue Value => Slot.Value ?? throw new InvalidOperationException("The variable is not a produced-value slot.");
    internal PlanLocal Local => Slot.Local ?? throw new InvalidOperationException("The variable is not a mutable-local slot.");
    internal bool IsLocal => Slot.Local is not null;
    internal Type LogicalType
    {
        get;
    }
    public string? Name => Slot.Name;

    internal static Variable Create(ExecutorScope plan, PlanValue value) => new UntypedVariable(plan, value);
    internal static Variable Create(ExecutorScope plan, PlanLocal local) => new UntypedVariable(plan, local);
    internal static Variable Create(ExecutorScope plan, PlanSlot slot) => slot.Value is PlanValue value ? new UntypedVariable(plan, value) : new UntypedVariable(plan, slot.Local ?? throw new InvalidOperationException("The variable slot has no storage."));

    private sealed class UntypedVariable : Variable
    {
        internal UntypedVariable(ExecutorScope plan, PlanValue value) : base(plan, value) { }
        internal UntypedVariable(ExecutorScope plan, PlanLocal local) : base(plan, local) { }
    }
}

/// <summary>Identifies one typed value slot in an executor plan.</summary>
/// <typeparam name="T">The logical type of the slot.</typeparam>
public sealed class Variable<T> : Variable
{
    internal Variable(ExecutorScope plan, PlanValue value) : base(plan, value) { }
    internal Variable(ExecutorScope plan, PlanLocal local) : base(plan, local) { }

    /// <summary>Provides the typed marker used only while recording an expression.</summary>
    /// <remarks>The expression reader replaces this marker with this variable. It is never read by the generated executor.</remarks>
    public T Val = default!;

    /// <summary>Uses this variable as a typed value while recording an expression.</summary>
    public static implicit operator T(Variable<T> variable)
        => throw new InvalidOperationException("Executor variables are only valid while recording an expression.");
}
