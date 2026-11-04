using System.Reflection;

namespace Rinku;

public sealed class StaticFieldReadOperation<T>(FieldInfo field, Variable<T> output) : Operation
{
    public FieldInfo Field { get; } = field.IsStatic ? field : throw new ArgumentException("The field must be static.", nameof(field));
    public Variable<T> Output { get; } = output;
    public override IPlanNode Copy(OperationCopyContext context) => new StaticFieldReadOperation<T>(Field, context.Copy(Output));
    public override IPlanNode? Resolve(OperationBindingContext context) => context.Bind(new(Field), [], Output);
    public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException("The operation must resolve before emission.");
}
