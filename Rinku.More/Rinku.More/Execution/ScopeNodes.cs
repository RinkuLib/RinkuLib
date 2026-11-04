using static Rinku.ExecutorCompiler;

namespace Rinku;

internal sealed class ScopeNode(PlanSequence body, PlanSlot? output = null) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences
    {
        get
        {
            yield return Body;
        }
    }

    internal PlanSlot? Output { get; set; } = output;
    internal PlanSequence Body { get; set; } = body;
    public override IPlanNode Copy(OperationCopyContext context) => new ScopeNode(context.Sequence(Body), Output is null ? null : context.Slot(Output));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.scopes.Push([]);
        try
        {
            Body = context.BindSequence(Body);
        }
        finally
        {
            context.scopes.Pop();
        }
        if (Output is not null && context.variables.TryGetValue(Output, out var bound) && bound.Bound)
        {
            context.Publish(Output);
        }
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitSequence(Body, context.breakLabel, context.continueLabel, context.inExceptionBlock);
    }
}

internal sealed class CompositionNode(ExecutorScope source, PlanValue? result = null) : PlanGroup
{
    public override IEnumerable<PlanSequence> Sequences => [Source.Body];
    internal ExecutorScope Source { get; set; } = source;
    internal PlanValue? Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new CompositionNode(context.Scope(Source), context.Value(Result));
    public override IPlanNode? Resolve(OperationBindingContext context) => context.BindScope(Source, Result);
    public override void Compile(OperationEmissionContext context)
    {
        throw new InvalidOperationException("The operation must resolve before emission.");
    }
}

