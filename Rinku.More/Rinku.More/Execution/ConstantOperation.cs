namespace Rinku;

public sealed class ConstantOperation<T>(T value, Variable<T> output) : Operation
{
    public T Value { get; } = value;
    public Variable<T> Output { get; } = output;
    public override IPlanNode Copy(OperationCopyContext context) => new ConstantOperation<T>(Value, context.Copy(Output));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        Type type = typeof(T).IsValueType || Value is null ? typeof(T) : Value.GetType();
        context.Define(Output, type, Value is null);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.Load(OperationArgument.Constant(Value), context.TypeOf(Output));
        context.Store(Output);
    }
}
