using static Rinku.OperationBindingContext;
using System.Reflection;
using System.Reflection.Emit;
using static Rinku.ExecutorCompiler;

namespace Rinku;

internal sealed class CallOperation(IReadOnlyList<string> names, OperationArgument[] operands, PlanValue? result, ExecutorArgumentPassing[] passing, Type[] genericArguments, Type? constructorType = null) : Operation
{
    internal IReadOnlyList<string> Names { get; } = names;
    internal ExecutorArgumentPassing[] Passing { get; } = passing;
    internal Type[] GenericArguments { get; } = genericArguments;
    internal Type? ConstructorType { get; } = constructorType;
    internal string? Name => ConstructorType?.Name ?? string.Join(" | ", Names);
    internal OperationArgument[] Operands { get; set; } = operands;
    internal PlanValue? Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new CallOperation(Names, Operands.Select(context.Argument).ToArray(), context.Value(Result), Passing, GenericArguments, ConstructorType);
    private IEnumerable<ExecutorCallCandidate> FindCandidates(OperationBindingContext context)
    {
        if (ConstructorType is not null)
        {
            foreach (ConstructorInfo constructor in ConstructorType.GetConstructors())
            {
                yield return new(constructor);
            }
            yield break;
        }
        foreach (string name in Names)
        {
            foreach (ExecutorCallCandidate candidate in context.methodPool.Find(name))
            {
                yield return candidate;
            }
        }
    }
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments(Operands.Where((_, index) => Passing[index] != ExecutorArgumentPassing.Out));
        Operands = context.ResolveReferenceOperands(Operands);

        foreach (OperationArgument operand in Operands)
        {
            if (operand.Variable is Variable variable)
            {
                if (context.variables.TryGetValue(variable.Slot, out VariableBinding argument) && argument.Bound)
                    context.methodPool.Include(argument.Type);
            }
            else if (operand.IsConstant)
            {
                context.methodPool.Include(context.GetArgumentType(operand));
            }
        }
        ExecutorCallCandidate selected = context.BindCall(FindCandidates(context), Operands, Passing, GenericArguments, Result?.LogicalType, Name!);
        return context.BindSelectedMember(selected, Operands, Result);
    }

    public override void Compile(OperationEmissionContext context)
    {
        throw new InvalidOperationException("The operation must resolve before emission.");
    }
}

internal sealed class BoundCallOperation(MethodBase method, OperationArgument[] operands, PlanValue? result) : Operation
{
    internal MethodBase Method { get; set; } = method;
    internal OperationArgument[] Operands { get; set; } = operands;
    internal PlanValue? Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new BoundCallOperation(Method, Operands.Select(context.Argument).ToArray(), context.Value(Result));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments(Operands);
        Operands = context.ResolveReferenceOperands(Operands);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        OperationArgument[] callOperands = Operands;
        if (Method is ConstructorInfo constructor)
        {
            context.EmitArguments(constructor, callOperands);
            context.il.Emit(OpCodes.Newobj, constructor);
            context.StoreResult(Result, constructor.DeclaringType ?? typeof(object));
            return;
        }

        MethodInfo method = (MethodInfo)Method;
        if (method.IsStatic)
        {
            context.EmitArguments(method, callOperands);
            context.il.Emit(OpCodes.Call, method);
            context.StoreResult(Result, method.ReturnType);
            return;
        }

        OperationArgument instance = callOperands[0];
        Type instanceType = context.GetArgumentType(instance);
        IReadOnlyList<OperationArgument> arguments = callOperands.Skip(1).ToArray();
        Type receiverType = context.EmitInstanceArgument(instance, method.DeclaringType ?? instanceType);
        context.EmitArguments(method, arguments);
        context.EmitInvocation(method, receiverType);
        context.StoreResult(Result, method.ReturnType);
    }
}

internal sealed class BoundFieldReadOperation(FieldInfo field, OperationArgument[] operands, PlanValue? result) : Operation
{
    internal FieldInfo Field { get; set; } = field;
    internal OperationArgument[] Operands { get; set; } = operands;
    internal PlanValue? Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new BoundFieldReadOperation(Field, Operands.Select(context.Argument).ToArray(), context.Value(Result));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments(Operands);
        Operands = context.ResolveReferenceOperands(Operands);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        FieldInfo field = Field;
        if (field.IsLiteral)
        {
            object? value = field.GetRawConstantValue();
            if (field.FieldType.IsEnum && value is not null)
                value = Enum.ToObject(field.FieldType, value);
            context.EmitArgument(OperationArgument.FromConstant(field.FieldType, value), field.FieldType);
        }
        else if (field.IsStatic)
        {
            context.il.Emit(OpCodes.Ldsfld, field);
        }
        else
        {
            context.EmitInstanceArgument(Operands[0], field.DeclaringType!);
            context.il.Emit(OpCodes.Ldfld, field);
        }
        context.StoreResult(Result, field.FieldType);
    }
}

internal sealed class BoundFieldWriteOperation(FieldInfo field, OperationArgument[] operands) : Operation
{
    internal FieldInfo Field { get; set; } = field;
    internal OperationArgument[] Operands { get; set; } = operands;
    public override IPlanNode Copy(OperationCopyContext context) => new BoundFieldWriteOperation(Field, Operands.Select(context.Argument).ToArray());
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments(Operands);
        Operands = context.ResolveReferenceOperands(Operands);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        FieldInfo field = Field;
        if (!field.IsStatic)
            context.EmitInstanceArgument(Operands[0], field.DeclaringType!);
        context.EmitArgument(Operands[^1], field.FieldType);
        context.il.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field);
    }
}

