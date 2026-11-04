using static Rinku.OperationBindingContext;
using System.Reflection;
using System.Reflection.Emit;
using static Rinku.ExecutorCompiler;

namespace Rinku;

internal sealed class ArrayCreateOperation(OperationArgument[] elements, PlanValue result) : Operation
{
    internal OperationArgument[] Elements { get; set; } = elements;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new ArrayCreateOperation(Elements.Select(context.Argument).ToArray(), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([.. Elements]);
        Elements = context.ResolveReferenceOperands(Elements);
        context.Define(Result, Result.LogicalType);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type actualElementType = context.plan.GetValueType(Result).GetElementType() ?? throw new InvalidOperationException("The array result has no element type.");
        Type actualArrayType = actualElementType.MakeArrayType();
        context.il.Emit(OpCodes.Ldc_I4, Elements.Length);
        context.il.Emit(OpCodes.Newarr, actualElementType);
        for (int i = 0; i < Elements.Length; i++)
        {
            context.il.Emit(OpCodes.Dup);
            context.il.Emit(OpCodes.Ldc_I4, i);
            context.EmitArgument(Elements[i], actualElementType);
            context.il.Emit(OpCodes.Stelem, actualElementType);
        }
        LocalBuilder local = context.il.DeclareLocal(actualArrayType);
        context.il.Emit(OpCodes.Stloc, local);
        context.values[Result] = new(actualArrayType, local, 0);
    }
}

internal sealed class ArrayReadOperation(OperationArgument array, OperationArgument index, PlanValue result) : Operation
{
    internal OperationArgument Array { get; set; } = array;
    internal OperationArgument Index { get; set; } = index;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new ArrayReadOperation(context.Argument(Array), context.Argument(Index), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Array, Index]);
        Array = context.ResolveReferenceOperand(Array);
        Index = context.ResolveReferenceOperand(Index);
        context.EnsureNotNull(Array);
        context.EnsureNotNull(Index);
        context.Define(Result, context.GetArgumentType(Array).GetElementType()!);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type actualArrayType = context.GetArgumentType(Array);
        Type actualElementType = actualArrayType.GetElementType() ?? throw new InvalidOperationException("The array argument has no element type.");
        context.EmitArgument(Array, actualArrayType);
        context.EmitArgument(Index, typeof(int));
        context.il.Emit(OpCodes.Ldelem, actualElementType);
        context.StoreResult(Result, actualElementType);
    }
}

internal sealed class ArrayAssignmentOperation(OperationArgument array, OperationArgument index, OperationArgument value) : Operation
{
    internal OperationArgument Array { get; set; } = array;
    internal OperationArgument Index { get; set; } = index;
    internal OperationArgument Value { get; set; } = value;
    public override IPlanNode Copy(OperationCopyContext context) => new ArrayAssignmentOperation(context.Argument(Array), context.Argument(Index), context.Argument(Value));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Array, Index, Value]);
        Array = context.ResolveReferenceOperand(Array);
        Index = context.ResolveReferenceOperand(Index);
        Value = context.ResolveReferenceOperand(Value);
        context.EnsureNotNull(Array);
        context.EnsureNotNull(Index);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type actualArrayType = context.GetArgumentType(Array);
        Type actualElementType = actualArrayType.GetElementType() ?? throw new InvalidOperationException("The array argument has no element type.");
        context.EmitArgument(Array, actualArrayType);
        context.EmitArgument(Index, typeof(int));
        context.EmitArgument(Value, actualElementType);
        context.il.Emit(OpCodes.Stelem, actualElementType);
    }
}

internal sealed class CapturedValueOperation(object? value, PlanValue result) : Operation
{
    internal object? Value { get; set; } = value;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new CapturedValueOperation(Value, context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.Define(Result, Result.LogicalType.IsValueType || Value is null ? Result.LogicalType : Value.GetType(), Value is null ? Availability.KnownNull : Availability.Available);
        context.constants[Result.Slot] = OperationArgument.FromConstant(context.TypeOf(Result), Value);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitArgument(OperationArgument.FromConstant(context.binding.TypeOf(Result), Value), context.binding.TypeOf(Result));
        context.StoreResult(Result, context.binding.TypeOf(Result));
    }
}

internal sealed class ConvertOperation(OperationArgument value, PlanValue result, bool isChecked = false) : Operation
{
    internal OperationArgument Value { get; set; } = value;
    internal PlanValue Result { get; set; } = result;
    internal bool IsChecked { get; set; } = isChecked;
    public override IPlanNode Copy(OperationCopyContext context) => new ConvertOperation(context.Argument(Value), context.Value(Result)!, IsChecked);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Value]);
        Value = context.ResolveReferenceOperand(Value);
        Type sourceType = context.GetArgumentType(Value);
        Type targetType = context.SpecializeType(Result.LogicalType);
        if (context.RequiresStructuralBinding(Result.LogicalType, sourceType) && Result.LogicalType.IsGenericType && TryGetGenericShape(sourceType, Result.LogicalType.GetGenericTypeDefinition(), out Type? shape) && shape is not null)
            targetType = shape;
        if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            context.EnsureNotNull(Value);
        context.Define(Result, targetType, context.State(Value));
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type sourceType = context.GetArgumentType(Value);
        context.EmitArgument(Value, sourceType);
        Type targetType = context.binding.TypeOf(Result);
        context.EmitConversion(sourceType, targetType, IsChecked);
        context.StoreResult(Result, targetType);
    }
}

internal sealed class TypeAsOperation(OperationArgument value, PlanValue result) : Operation
{
    internal OperationArgument Value { get; set; } = value;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new TypeAsOperation(context.Argument(Value), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Value]);
        Value = context.ResolveReferenceOperand(Value);
        context.Define(Result, context.SpecializeType(Result.LogicalType), context.State(Value));
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitArgument(Value, typeof(object));
        Type targetType = context.binding.TypeOf(Result);
        context.il.Emit(OpCodes.Isinst, targetType);
        context.StoreResult(Result, targetType);
    }
}

internal sealed class TypeTestOperation(OperationArgument value, Type type, PlanValue result) : Operation
{
    internal OperationArgument Value { get; set; } = value;
    internal Type Type { get; set; } = type;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new TypeTestOperation(context.Argument(Value), Type, context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Value]);
        Value = context.ResolveReferenceOperand(Value);
        context.Define(Result, typeof(bool));
        return new TypeTestOperation(Value, context.SpecializeType(Type), Result);
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitArgument(Value, typeof(object));
        context.il.Emit(OpCodes.Isinst, Type);
        context.il.Emit(OpCodes.Ldnull);
        context.il.Emit(OpCodes.Cgt_Un);
        context.StoreResult(Result, typeof(bool));
    }
}

internal sealed class BinaryOperation(OperationArgument left, ExecutorBinaryOperator @operator, OperationArgument right, PlanValue result, bool isChecked = false) : Operation
{
    internal OperationArgument Left { get; set; } = left;
    internal ExecutorBinaryOperator Operator { get; set; } = @operator;
    internal OperationArgument Right { get; set; } = right;
    internal PlanValue Result { get; set; } = result;
    internal bool IsChecked { get; set; } = isChecked;
    public override IPlanNode Copy(OperationCopyContext context) => new BinaryOperation(context.Argument(Left), Operator, context.Argument(Right), context.Value(Result)!, IsChecked);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Left, Right]);
        Left = context.ResolveReferenceOperand(Left);
        Right = context.ResolveReferenceOperand(Right);
        Type left = context.GetArgumentType(Left);
        Type right = context.GetArgumentType(Right);
        Type binaryType = Operator == ExecutorBinaryOperator.Add && (context.GetArgumentType(Left) == typeof(string) || context.GetArgumentType(Right) == typeof(string)) ? typeof(string)
        : Operator is ExecutorBinaryOperator.LeftShift or ExecutorBinaryOperator.RightShift ? GetShiftResultType(left)
        : Operator is ExecutorBinaryOperator.And or ExecutorBinaryOperator.Or or ExecutorBinaryOperator.ExclusiveOr ? GetBitwiseResultType(left, right)
        : IsNumeric(left) && IsNumeric(right) ? GetNumericPromotionType(left, right) : context.SpecializeType(Result.LogicalType);
        if (binaryType != typeof(string))
        {
            context.EnsureNotNull(Left);
            context.EnsureNotNull(Right);
        }
        context.Define(Result, binaryType);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type leftType = context.GetArgumentType(Left);
        Type rightType = context.GetArgumentType(Right);
        if (Operator == ExecutorBinaryOperator.Add && (leftType == typeof(string) || rightType == typeof(string)))
        {
            context.EmitArgument(Left, typeof(object));
            context.EmitArgument(Right, typeof(object));
            MethodInfo concat = typeof(string).GetMethod(nameof(string.Concat), [typeof(object), typeof(object)]) ?? throw new InvalidOperationException("string.Concat is missing.");
            context.il.Emit(OpCodes.Call, concat);
            context.StoreResult(Result, typeof(string));
            return;
        }
        bool isShift = Operator is ExecutorBinaryOperator.LeftShift or ExecutorBinaryOperator.RightShift;
        Type operationType = context.binding.TypeOf(Result);
        if (operationType == typeof(decimal))
        {
            context.EmitArgument(Left, operationType);
            context.EmitArgument(Right, operationType);
            MethodInfo method = typeof(decimal).GetMethod(OperationEmissionContext.GetDecimalBinaryMethod(Operator), [typeof(decimal), typeof(decimal)]) ?? throw new InvalidOperationException($"Decimal operation {Operator} is missing.");
            context.il.Emit(OpCodes.Call, method);
            context.StoreResult(Result, typeof(decimal));
            return;
        }
        context.EmitArgument(Left, operationType);
        context.EmitArgument(Right, isShift ? typeof(int) : operationType);
        context.il.Emit(Operator switch
        {
            ExecutorBinaryOperator.Add => IsChecked ? (IsUnsigned(operationType) ? OpCodes.Add_Ovf_Un : OpCodes.Add_Ovf) : OpCodes.Add,
            ExecutorBinaryOperator.Subtract => IsChecked ? (IsUnsigned(operationType) ? OpCodes.Sub_Ovf_Un : OpCodes.Sub_Ovf) : OpCodes.Sub,
            ExecutorBinaryOperator.Multiply => IsChecked ? (IsUnsigned(operationType) ? OpCodes.Mul_Ovf_Un : OpCodes.Mul_Ovf) : OpCodes.Mul,
            ExecutorBinaryOperator.Divide => IsUnsigned(operationType) ? OpCodes.Div_Un : OpCodes.Div,
            ExecutorBinaryOperator.Modulo => IsUnsigned(operationType) ? OpCodes.Rem_Un : OpCodes.Rem,
            ExecutorBinaryOperator.And => OpCodes.And,
            ExecutorBinaryOperator.Or => OpCodes.Or,
            ExecutorBinaryOperator.ExclusiveOr => OpCodes.Xor,
            ExecutorBinaryOperator.LeftShift => OpCodes.Shl,
            ExecutorBinaryOperator.RightShift => IsUnsigned(operationType) ? OpCodes.Shr_Un : OpCodes.Shr,
            _ => throw new ArgumentOutOfRangeException(nameof(Operator))
        });
        context.StoreResult(Result, operationType);
    }
}

internal sealed class CompareOperation(OperationArgument left, ExecutorComparison comparison, OperationArgument right, PlanValue result) : Operation
{
    internal OperationArgument Left { get; set; } = left;
    internal ExecutorComparison Comparison { get; set; } = comparison;
    internal OperationArgument Right { get; set; } = right;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new CompareOperation(context.Argument(Left), Comparison, context.Argument(Right), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Left, Right]);
        Left = context.ResolveReferenceOperand(Left);
        Right = context.ResolveReferenceOperand(Right);
        context.Define(Result, typeof(bool));
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type leftType = context.GetArgumentType(Left);
        Type rightType = context.GetArgumentType(Right);
        if (Nullable.GetUnderlyingType(leftType) is not null || Nullable.GetUnderlyingType(rightType) is not null)
        {
            EmitNullableCompare(context, leftType, rightType);
            return;
        }
        Type comparisonType = IsNumeric(leftType) && IsNumeric(rightType) ? GetNumericPromotionType(leftType, rightType) : GetComparisonType(leftType, rightType);
        if (comparisonType == typeof(decimal))
        {
            context.EmitArgument(Left, comparisonType);
            context.EmitArgument(Right, comparisonType);
            MethodInfo compare = typeof(decimal).GetMethod(nameof(decimal.Compare), [typeof(decimal), typeof(decimal)]) ?? throw new InvalidOperationException("Decimal.Compare is missing.");
            context.il.Emit(OpCodes.Call, compare);
            context.EmitCompareZero(Comparison);
        }
        else
        {
            context.EmitArgument(Left, comparisonType);
            context.EmitArgument(Right, comparisonType);
            if (comparisonType == typeof(object) && (leftType.IsValueType || rightType.IsValueType))
            {
                MethodInfo equals = typeof(object).GetMethod(nameof(object.Equals), [typeof(object), typeof(object)]) ?? throw new InvalidOperationException("object.Equals is missing.");
                context.il.Emit(OpCodes.Call, equals);
                if (Comparison == ExecutorComparison.NotEqual)
                {
                    context.il.Emit(OpCodes.Ldc_I4_0);
                    context.il.Emit(OpCodes.Ceq);
                }
                context.StoreResult(Result, typeof(bool));
                return;
            }
            context.EmitComparison(Comparison, comparisonType);
        }
        context.StoreResult(Result, typeof(bool));
    }

    private void EmitNullableCompare(OperationEmissionContext context, Type leftType, Type rightType)
    {
        Type leftValueType = Nullable.GetUnderlyingType(leftType) ?? leftType;
        Type rightValueType = Nullable.GetUnderlyingType(rightType) ?? rightType;
        Type comparisonType = IsNumeric(leftValueType) && IsNumeric(rightValueType) ? GetNumericPromotionType(leftValueType, rightValueType) : GetComparisonType(leftValueType, rightValueType);
        bool leftNullable = Nullable.GetUnderlyingType(leftType) is not null;
        bool rightNullable = Nullable.GetUnderlyingType(rightType) is not null;
        LocalBuilder left = context.il.DeclareLocal(leftType);
        LocalBuilder right = context.il.DeclareLocal(rightType);
        LocalBuilder result = context.il.DeclareLocal(typeof(bool));
        Label compare = context.il.DefineLabel();
        Label leftMissing = context.il.DefineLabel();
        Label rightMissing = context.il.DefineLabel();
        Label end = context.il.DefineLabel();

        context.EmitArgument(Left, leftType);
        context.il.Emit(OpCodes.Stloc, left);
        context.EmitArgument(Right, rightType);
        context.il.Emit(OpCodes.Stloc, right);
        if (leftNullable)
        {
            context.il.Emit(OpCodes.Ldloca, left);
            context.il.Emit(OpCodes.Call, OperationEmissionContext.GetNullableAccessor(leftType, nameof(Nullable<int>.HasValue)));
            context.il.Emit(OpCodes.Brfalse, leftMissing);
        }
        if (rightNullable)
        {
            context.il.Emit(OpCodes.Ldloca, right);
            context.il.Emit(OpCodes.Call, OperationEmissionContext.GetNullableAccessor(rightType, nameof(Nullable<int>.HasValue)));
            context.il.Emit(OpCodes.Brfalse, rightMissing);
        }
        context.il.Emit(OpCodes.Br, compare);

        context.il.MarkLabel(leftMissing);
        if (rightNullable && Comparison is (ExecutorComparison.Equal or ExecutorComparison.NotEqual))
        {
            Label rightHasValue = context.il.DefineLabel();
            context.il.Emit(OpCodes.Ldloca, right);
            context.il.Emit(OpCodes.Call, OperationEmissionContext.GetNullableAccessor(rightType, nameof(Nullable<int>.HasValue)));
            context.il.Emit(OpCodes.Brtrue, rightHasValue);
            context.il.Emit(Comparison == ExecutorComparison.Equal ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            context.il.Emit(OpCodes.Br, end);
            context.il.MarkLabel(rightHasValue);
            context.il.Emit(Comparison == ExecutorComparison.NotEqual ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            context.il.Emit(OpCodes.Br, end);
        }
        context.il.Emit(Comparison == ExecutorComparison.NotEqual ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        context.il.Emit(OpCodes.Br, end);

        context.il.MarkLabel(rightMissing);
        context.il.Emit(Comparison == ExecutorComparison.NotEqual ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        context.il.Emit(OpCodes.Br, end);

        context.il.MarkLabel(compare);
        context.EmitNullableValue(left, leftType, leftValueType, comparisonType);
        context.EmitNullableValue(right, rightType, rightValueType, comparisonType);
        if (comparisonType == typeof(decimal))
        {
            MethodInfo decimalCompare = typeof(decimal).GetMethod(nameof(decimal.Compare), [typeof(decimal), typeof(decimal)]) ?? throw new InvalidOperationException("Decimal.Compare is missing.");
            context.il.Emit(OpCodes.Call, decimalCompare);
            context.EmitCompareZero(Comparison);
        }
        else if (comparisonType == typeof(object) && (leftValueType.IsValueType || rightValueType.IsValueType) && Comparison is (ExecutorComparison.Equal or ExecutorComparison.NotEqual))
        {
            MethodInfo equals = typeof(object).GetMethod(nameof(object.Equals), [typeof(object), typeof(object)]) ?? throw new InvalidOperationException("object.Equals is missing.");
            context.il.Emit(OpCodes.Call, equals);
            if (Comparison == ExecutorComparison.NotEqual)
            {
                context.il.Emit(OpCodes.Ldc_I4_0);
                context.il.Emit(OpCodes.Ceq);
            }
        }
        else
            context.EmitComparison(Comparison, comparisonType);
        context.il.Emit(OpCodes.Br, end);

        context.il.MarkLabel(end);
        context.il.Emit(OpCodes.Stloc, result);
        context.values[Result] = new(typeof(bool), result, 0);
    }


}
internal sealed class LogicalNotOperation(OperationArgument value, PlanValue result) : Operation
{
    internal OperationArgument Value { get; set; } = value;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new LogicalNotOperation(context.Argument(Value), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Value]);
        Value = context.ResolveReferenceOperand(Value);
        context.EnsureNotNull(Value);
        context.Define(Result, typeof(bool));
        if (Value.Variable is Variable source && context.conditionalNonNull.TryGetValue(source.Slot, out var contracts))
            context.conditionalNonNull[Result.Slot] = contracts.Select(contract => (contract.Slot, !contract.When, contract.Write)).ToArray();
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitArgument(Value, typeof(bool));
        context.il.Emit(OpCodes.Ldc_I4_0);
        context.il.Emit(OpCodes.Ceq);
        context.StoreResult(Result, typeof(bool));
    }
}

internal sealed class UnaryOperation(OperationArgument value, ExecutorUnaryOperator @operator, PlanValue result, bool isChecked = false) : Operation
{
    internal OperationArgument Value { get; set; } = value;
    internal ExecutorUnaryOperator Operator { get; set; } = @operator;
    internal PlanValue Result { get; set; } = result;
    internal bool IsChecked { get; set; } = isChecked;
    public override IPlanNode Copy(OperationCopyContext context) => new UnaryOperation(context.Argument(Value), Operator, context.Value(Result)!, IsChecked);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Value]);
        Value = context.ResolveReferenceOperand(Value);
        context.EnsureNotNull(Value);
        context.Define(Result, ExecutorBuilder.GetUnaryResultType(context.GetArgumentType(Value), Operator));
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Type operationType = context.binding.TypeOf(Result);
        if (Operator == ExecutorUnaryOperator.OnesComplement)
        {
            context.EmitArgument(Value, operationType);
            context.il.Emit(OpCodes.Not);
        }
        else if (operationType == typeof(decimal))
        {
            context.EmitArgument(Value, operationType);
            MethodInfo negate = typeof(decimal).GetMethod(nameof(decimal.Negate), [typeof(decimal)]) ?? throw new InvalidOperationException("Decimal.Negate is missing.");
            context.il.Emit(OpCodes.Call, negate);
        }
        else if (operationType == typeof(sbyte) || operationType == typeof(byte) || operationType == typeof(short) || operationType == typeof(ushort) || operationType == typeof(int) || operationType == typeof(long) || operationType == typeof(float) || operationType == typeof(double))
        {
            if (IsChecked && operationType != typeof(float) && operationType != typeof(double))
            {
                if (operationType == typeof(long))
                    context.il.Emit(OpCodes.Ldc_I8, 0L);
                else
                    context.il.Emit(OpCodes.Ldc_I4_0);
                context.EmitArgument(Value, operationType);
                context.il.Emit(IsUnsigned(operationType) ? OpCodes.Sub_Ovf_Un : OpCodes.Sub_Ovf);
            }
            else
            {
                context.EmitArgument(Value, operationType);
                context.il.Emit(OpCodes.Neg);
            }
        }
        else
            throw new InvalidOperationException($"Unary operation {Operator} is not supported for {operationType}.");
        context.StoreResult(Result, operationType);
    }
}

internal sealed class StoreOperation(OperationArgument target, OperationArgument source) : Operation
{
    internal OperationArgument Target { get; set; } = target;
    internal OperationArgument Source { get; set; } = source;
    public override IPlanNode Copy(OperationCopyContext context) => new StoreOperation(context.Argument(Target), context.Argument(Source));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.RequireArguments([Source]);
        Target = context.ResolveReferenceOperand(Target);
        Source = context.ResolveReferenceOperand(Source);
        Variable variable = Target.Variable ?? throw new ExecutorBindingException(ExecutorBindingFailure.TypeMismatch, "A fixed value cannot be assigned.");
        context.StoreSlot(variable.Slot, Source);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        Variable variable = Target.Variable!;
        context.EmitArgument(Source, context.binding.TypeOf(variable));
        context.Store(variable);
    }
}

internal sealed class LocalLoadOperation(PlanLocal local, PlanValue result) : Operation
{
    internal PlanLocal Local { get; set; } = local;
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new LocalLoadOperation(context.Local(Local), context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.Define(Result, context.GetSlotType(Local.Slot), context.State(Local.Slot));
        context.CopyContracts(Local.Slot, Result.Slot);
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        if (!context.locals.TryGetValue(Local, out var local))
            throw new InvalidOperationException($"Local {Local.Name ?? Local.LogicalType.Name} was loaded before it was stored.");
        context.values[Result] = local;
    }
}

internal sealed class NullOperation(PlanSlot slot, Type type) : Operation
{
    internal PlanSlot Slot { get; set; } = slot;
    internal Type Type { get; set; } = type;
    public override IPlanNode Copy(OperationCopyContext context) => new NullOperation(context.Slot(Slot), Type);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        return this;
    }
    public override void Compile(OperationEmissionContext context)
    {
        context.EmitNull(Type);
        if (Slot.Value is PlanValue value)
            context.StoreResult(value, Type);
        else
            context.EmitStore(context.locals[Slot.Local!]);
    }
}


internal sealed class ReferenceOperation(PlanValue result) : Operation
{
    internal PlanValue Result { get; set; } = result;
    public override IPlanNode Copy(OperationCopyContext context) => new ReferenceOperation(context.Value(Result)!);
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.references[Result.Slot] = context.ResolveReference(Result.Slot);
        return null;
    }
    public override void Compile(OperationEmissionContext context)
    {
    }
}

internal sealed class DeclarationOperation(PlanLocal local) : Operation
{
    internal PlanLocal Local { get; set; } = local;
    public override IPlanNode Copy(OperationCopyContext context) => new DeclarationOperation(context.Local(Local));
    public override IPlanNode? Resolve(OperationBindingContext context)
    {
        context.Publish(Local.Slot);
        return null;
    }
    public override void Compile(OperationEmissionContext context)
    {
    }
}

