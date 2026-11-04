namespace Rinku;

public sealed class PlanSequence
{
    private readonly List<PlanInstruction> items;
    public PlanSequence(int capacity = 0) => items = new(capacity);
    public IReadOnlyList<PlanInstruction> Items => items;
    public void Add(IPlanNode node, ExecutorRequirement requirement = ExecutorRequirement.Required, params Variable[] outputs) => items.Add(new(node, requirement, outputs));
    internal void Add(PlanInstruction instruction) => items.Add(instruction);
    internal void ReplaceAt(int index, PlanInstruction instruction) => items[index] = instruction;
    internal void RemoveAt(int index) => items.RemoveAt(index);
    internal void RemoveFrom(int index) => items.RemoveRange(index, items.Count - index);
}

