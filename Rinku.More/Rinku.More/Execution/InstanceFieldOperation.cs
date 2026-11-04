namespace Rinku;

internal sealed class InstanceFieldOperation(string? name, PlanLocal local) : Operation
{
    public override IPlanNode Copy(OperationCopyContext context) => new InstanceFieldOperation(name, context.Local(local));
    public override IPlanNode Resolve(OperationBindingContext context)
    {
        context.Define(local.Slot, local.LogicalType, OperationBindingContext.Availability.Available);
        context.InstanceFields.Add(local.Slot, (name, local.LogicalType));
        return this;
    }
    public override void Compile(OperationEmissionContext context) { }
}
