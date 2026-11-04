namespace Rinku;

public interface IOperation : IPlanNode;

public abstract class Operation : PlanNode, IOperation;
