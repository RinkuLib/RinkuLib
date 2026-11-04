using System.Reflection;
using System.Reflection.Emit;

namespace Rinku;

internal sealed class DelegateOperation(Type delegateType, ExecutorScope body, OperationArgument[] captures, PlanValue result, bool methodGroup = false) : Operation
{
    public override IPlanNode Copy(OperationCopyContext context) => new DelegateOperation(delegateType, ExecutorCompiler.CloneScope(body), captures.Select(context.Argument).ToArray(), context.Value(result)!, methodGroup);

    public override IPlanNode Resolve(OperationBindingContext context)
    {
        OperationArgument[] actual = context.ResolveReferenceOperands(captures);
        context.RequireArguments(actual);
        MethodInfo signature = delegateType.GetMethod(nameof(Action.Invoke))!;
        Type[] inputs = actual.Select(context.TypeOf).Concat(signature.GetParameters().Select(parameter => parameter.ParameterType)).ToArray();
        OperationBindingContext nested = new(body, context.methodPool.Clone(), inputs);
        for (int index = 0; index < actual.Length; index++)
        {
            nested.Define(body.Inputs[index], inputs[index], context.State(actual[index]));
        }
        foreach (Type input in inputs)
        {
            nested.methodPool.Include(input);
        }
        nested.Body = nested.BindPlan(body.Body, [signature]);
        context.Define(result, delegateType);
        if (methodGroup && nested.Body.Items.Count == 1 && nested.Body.Items[0].Node is BoundCallOperation { Method: MethodInfo method } call && actual.Length == (method.IsStatic ? 0 : 1) && method.DeclaringType?.IsValueType != true) {
            var parameters = method.GetParameters();
            var expected = signature.GetParameters();
            bool compatible = parameters.Length == expected.Length && (method.ReturnType == signature.ReturnType || !method.ReturnType.IsValueType && signature.ReturnType.IsAssignableFrom(method.ReturnType));
            for (int index = 0; compatible && index < parameters.Length; index++)
                compatible = parameters[index].ParameterType == expected[index].ParameterType || !expected[index].ParameterType.IsValueType && parameters[index].ParameterType.IsAssignableFrom(expected[index].ParameterType);
            for (int index = 0; compatible && index < call.Operands.Length; index++)
                compatible = OperationArgumentComparer.Instance.Equals(call.Operands[index], OperationArgument.FromValue(nested.Plan.Inputs[index]));
            if (compatible) {
                context.AccessTypes.UnionWith(nested.AccessTypes);
                return new BoundMethodDelegateOperation(delegateType, method, actual.Length == 0 ? (OperationArgument?)null : actual[0], result);
            }
        }
        return new BoundDelegateOperation(delegateType, nested, actual, result);
    }

    public override void Compile(OperationEmissionContext context) => throw new InvalidOperationException("A delegate must resolve before emission.");
}

internal sealed class BoundDelegateOperation(Type delegateType, OperationBindingContext binding, OperationArgument[] captures, PlanValue result) : Operation
{
    internal Type DelegateType { get; } = delegateType;
    internal OperationBindingContext Binding { get; } = binding;
    internal OperationArgument[] Captures { get; } = captures;
    internal PlanValue Result { get; } = result;
    internal MethodInfo? Method { get; set; }
    public override IPlanNode Copy(OperationCopyContext context) => throw new InvalidOperationException("A bound delegate cannot be recorded.");
    public override IPlanNode Resolve(OperationBindingContext context) => this;
    public override void Compile(OperationEmissionContext context) => context.EmitDelegate(this);
}

internal sealed class BoundMethodDelegateOperation(Type delegateType, MethodInfo method, OperationArgument? target, PlanValue result) : Operation {
    private readonly Delegate? _static = method.IsStatic ? method.CreateDelegate(delegateType) : null;
    public override IPlanNode Copy(OperationCopyContext context) => throw new InvalidOperationException("A bound delegate cannot be recorded.");
    public override IPlanNode Resolve(OperationBindingContext context) => this;
    public override void Compile(OperationEmissionContext context) {
        if (_static is not null) context.Load(OperationArgument.FromTypedConstant(delegateType, _static), delegateType);
        else {
            context.Load(target!.Value, method.DeclaringType!);
            if (method.IsVirtual) { context.IL.Emit(OpCodes.Dup); context.IL.Emit(OpCodes.Ldvirtftn, method); }
            else context.IL.Emit(OpCodes.Ldftn, method);
            context.IL.Emit(OpCodes.Newobj, delegateType.GetConstructor([typeof(object), typeof(IntPtr)])!);
        }
        context.StoreResult(result, delegateType);
    }
}

