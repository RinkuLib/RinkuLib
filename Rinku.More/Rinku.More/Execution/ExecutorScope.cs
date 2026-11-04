namespace Rinku;

public sealed class ExecutorScope : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences => [Body];
    public override IPlanNode Copy(OperationCopyContext context) => context.Scope(this);
    public override IPlanNode? Resolve(OperationBindingContext context) => context.BindScope(this);
    public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException("A recorded scope must resolve before emission.");
    private readonly List<PlanSlot> _slots = [];
    internal Dictionary<(string Name, int Arity), ExecutorScope> Methods { get; } = [];
    internal HashSet<Type> MethodContracts { get; } = [];
    public ExecutorScope() : this([]) { }
    internal ExecutorScope(Type[] inputTypes) => Inputs = inputTypes.Select(type => CreateValue(type, true)).ToArray();
    public PlanSequence Instructions => Body;
    public Variable? Result => Output;

    internal Variable? Output
    {
        get; set;
    }

    internal IReadOnlyList<PlanValue> Inputs
    {
        get;
    }
    internal PlanSequence Body { get; } = new();
    internal IReadOnlyList<PlanSlot> Slots => _slots;

    internal void AddSlot(PlanSlot slot) => _slots.Add(slot);

    internal PlanSlot GetSlot(int index)
    {
        if ((uint)index >= (uint)_slots.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _slots[index];
    }

    internal PlanValue CreateValue(Type logicalType, bool input = false)
    {
        PlanSlot slot = new(this, logicalType, input ? PlanSlotKind.Input : PlanSlotKind.Value);
        PlanValue value = new(slot);
        slot.Value = value;
        _slots.Add(slot);
        return value;
    }

    internal PlanValue CreateVariable(Type logicalType, string? name)
    {
        PlanValue value = CreateValue(logicalType);
        PublishValue(value, name);
        return value;
    }

    internal void PublishValue(PlanValue value, string? name)
    {
        EnsurePlan(value);
        if (value.IsVariable)
        {
            if (name is not null && !string.Equals(value.Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException($"The variable of type {value.LogicalType} is already named '{value.Name ?? "<unnamed>"}'.");
            return;
        }

        value.Name = name;
        value.IsVariable = true;
    }

    internal PlanLocal CreateLocal(Type logicalType, string? name)
    {
        PlanSlot slot = new(this, logicalType, PlanSlotKind.Local);
        PlanLocal local = new(slot, name);
        slot.Local = local;
        slot.Name = name;
        slot.IsPublished = true;
        _slots.Add(slot);
        return local;
    }

    internal void Add(PlanSequence sequence, IPlanNode node, ExecutorRequirement requirement = ExecutorRequirement.Required, params Variable[] outputs) => sequence.Add(node, requirement, outputs);

    internal void Rollback(PlanSequence sequence, int itemCount, int slotCount)
    {
        sequence.RemoveFrom(itemCount);
        if (slotCount < _slots.Count)
            _slots.RemoveRange(slotCount, _slots.Count - slotCount);
    }

    internal Type GetValueType(PlanValue value)
    {
        EnsurePlan(value);
        return value.LogicalType;
    }
    internal Type GetLocalType(PlanLocal local)
    {
        EnsurePlan(local);
        return local.LogicalType;
    }

    internal void EnsurePlan(PlanValue value)
    {
        if (!ReferenceEquals(value.Plan, this) || !_slots.Contains(value.Slot))
            throw new InvalidOperationException("The executor value belongs to another builder.");
    }

    internal void EnsurePlan(PlanLocal local)
    {
        if (!ReferenceEquals(local.Plan, this) || !_slots.Contains(local.Slot))
            throw new InvalidOperationException("The executor local belongs to another builder.");
    }
}

