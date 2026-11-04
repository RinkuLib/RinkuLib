namespace Rinku;

internal enum PlanSlotKind : byte
{
    Input, Value, Local, Reference
}

internal sealed class PlanSlot(ExecutorScope plan, Type logicalType, PlanSlotKind kind)
{
    internal ExecutorScope Plan { get; } = plan;
    internal Type LogicalType { get; } = logicalType;
    internal PlanSlotKind Kind { get; } = kind;
    internal string? Name
    {
        get; set;
    }
    internal bool IsPublished
    {
        get; set;
    }
    internal bool DefaultWhenMissing
    {
        get; set;
    }
    internal bool IsInput => Kind == PlanSlotKind.Input;
    internal PlanValue? Value
    {
        get; set;
    }
    internal PlanLocal? Local
    {
        get; set;
    }
}

internal sealed class PlanValue
{
    internal PlanValue(PlanSlot slot) => Slot = slot;
    internal PlanSlot Slot
    {
        get;
    }
    internal ExecutorScope Plan => Slot.Plan;
    internal Type LogicalType => Slot.LogicalType;
    internal string? Name
    {
        get => Slot.Name; set => Slot.Name = value;
    }
    internal bool IsVariable
    {
        get => Slot.IsPublished; set => Slot.IsPublished = value;
    }
    internal bool IsInput => Slot.IsInput;
}

internal sealed class PlanLocal
{
    internal PlanLocal(PlanSlot slot, string? name) => (Slot, Name) = (slot, name);
    internal PlanSlot Slot
    {
        get;
    }
    internal ExecutorScope Plan => Slot.Plan;
    internal Type LogicalType => Slot.LogicalType;
    internal string? Name
    {
        get;
    }
}

