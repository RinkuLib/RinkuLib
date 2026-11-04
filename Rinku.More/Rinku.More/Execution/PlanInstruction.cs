namespace Rinku;

public sealed class PlanInstruction
{
    public PlanInstruction(IPlanNode node, ExecutorRequirement requirement = ExecutorRequirement.Required, params Variable[] outputs)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is IOperation && node is IPlanGroup)
        {
            throw new ArgumentException("An operation must be a leaf node.", nameof(node));
        }
        Node = node;
        Requirement = requirement;
        Outputs = outputs;
    }
    public IPlanNode Node
    {
        get;
    }
    private ExecutorRequirement requirement;
    public ExecutorRequirement Requirement
    {
        get => requirement;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            requirement = value;
        }
    }
    public IReadOnlyList<Variable> Outputs
    {
        get;
    }
    internal IEnumerable<Variable> AllOutputs
    {
        get
        {
            foreach (Variable output in Outputs)
            {
                yield return output;
            }
            if (Node is IPlanGroup group && Node is not (ExecutorScope or CompositionNode))
            {
                foreach (PlanSequence sequence in group.Sequences)
                {
                    foreach (PlanInstruction instruction in sequence.Items)
                    {
                        foreach (Variable output in instruction.AllOutputs)
                        {
                            yield return output;
                        }
                    }
                }
            }
        }
    }
    internal IEnumerable<Variable> Exports => Node switch
    {
        ExecutorScope => [],
        ScopeNode scope => scope.Output is null ? [] : [Variable.Create(scope.Output.Plan, scope.Output)],
        CompositionNode composition => composition.Result is null ? [] : [Variable.Create(composition.Result.Plan, composition.Result)],
        _ => AllOutputs
    };
    internal PlanInstruction Copy(OperationCopyContext context) => new(Node.Copy(context), Requirement, Outputs.Select(output => context.Argument(output).Variable!).ToArray());
}
