namespace Rinku;

public interface IPlanNode
{
    IPlanNode Copy(OperationCopyContext context);
    IPlanNode? Resolve(OperationBindingContext context);
    void Compile(OperationEmissionContext context);
}

public abstract class PlanNode : IPlanNode
{
    public abstract IPlanNode Copy(OperationCopyContext context);
    public abstract IPlanNode? Resolve(OperationBindingContext context);
    public abstract void Compile(OperationEmissionContext context);
}

public interface IPlanGroup : IPlanNode
{
    IEnumerable<PlanSequence> Sequences
    {
        get;
    }
}

public abstract class PlanGroup : PlanNode, IPlanGroup
{
    public abstract IEnumerable<PlanSequence> Sequences
    {
        get;
    }
}
