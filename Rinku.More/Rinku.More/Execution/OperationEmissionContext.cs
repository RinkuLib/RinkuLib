using System.Reflection;
using System.Reflection.Emit;
using static Rinku.ExecutorCompiler;

namespace Rinku;

public sealed class OperationEmissionContext
{
    internal readonly ILGenerator il;
    internal readonly ExecutorScope plan;
    internal readonly PlanSequence body;
    internal readonly Func<OperationArgument, FieldBuilder> constantField;

    internal readonly ExecutorEntry entry;
    internal readonly OperationBindingContext binding;
    internal readonly Dictionary<PlanValue, ValueLocation> values = [];
    internal readonly Dictionary<PlanLocal, ValueLocation> locals = [];
    private readonly IReadOnlyDictionary<PlanSlot, FieldBuilder> capturedFields;
    private readonly Func<BoundDelegateOperation, FieldBuilder> delegateField;
    private readonly int captureCount;
    internal readonly Label returnLabel;

    internal OperationEmissionContext(ILGenerator il, ExecutorScope plan, PlanSequence body, Func<OperationArgument, FieldBuilder> constantField, ExecutorEntry entry, OperationBindingContext binding, IReadOnlyDictionary<PlanSlot, FieldBuilder> capturedFields, Func<BoundDelegateOperation, FieldBuilder> delegateField, int captureCount)
    {
        this.il = il;
        this.plan = plan;
        this.body = body;
        this.constantField = constantField;
        this.entry = entry;
        this.binding = binding;
        this.capturedFields = capturedFields;
        this.delegateField = delegateField;
        this.captureCount = captureCount;
        returnLabel = il.DefineLabel();
    }

    public void Compile(PlanSequence sequence) => EmitSequence(sequence, breakLabel, continueLabel, inExceptionBlock);
    public ILGenerator IL => il;
    public Type TypeOf(Variable variable) => binding.TypeOf(variable);
    public void Load(OperationArgument argument, Type? expectedType = null) => EmitArgument(argument, expectedType ?? binding.TypeOf(argument));
    public void Store(Variable variable)
    {
        if (variable.IsLocal)
        {
            EmitStore(locals[variable.Local]);
        }
        else
        {
            StoreResult(variable.Value, binding.TypeOf(variable));
        }
    }

    internal void EmitEntry()
    {
        for (int i = 0; i < plan.Inputs.Count; i++)
        {
            PlanValue input = plan.Inputs[i];
            Type inputType = binding.TypeOf(input);
            if (i < captureCount)
            {
                if (!capturedFields.TryGetValue(input.Slot, out FieldBuilder? field))
                    throw new InvalidOperationException("A callback capture has no retained storage.");
                values[input] = new(inputType, null, 0, field);
            }
            else
            {
                values[input] = new(inputType, null, i - captureCount + 1);
            }
        }
        foreach (var local in binding.LocalTypes)
        {
            locals[local.Key] = capturedFields.TryGetValue(local.Key.Slot, out FieldBuilder? field) ? new(local.Value, null, 0, field) : new(local.Value, il.DeclareLocal(local.Value), 0);
        }
        EmitSequence(body, null, null, false);
        il.MarkLabel(returnLabel);
        if (entry.ReturnType != typeof(void))
            EmitArgument(binding.Result, entry.ReturnType);
        il.Emit(OpCodes.Ret);
    }

    internal Label? breakLabel;
    internal Label? continueLabel;
    internal bool inExceptionBlock;
    internal void EmitSequence(PlanSequence sequence, Label? breakLabel, Label? continueLabel, bool inExceptionBlock)
    {
        var previous = (this.breakLabel, this.continueLabel, this.inExceptionBlock);
        this.breakLabel = breakLabel;
        this.continueLabel = continueLabel;
        this.inExceptionBlock = inExceptionBlock;
        try
        {
            foreach (PlanInstruction instruction in sequence.Items)
            {
                instruction.Node.Compile(this);
            }
        }
        finally
        {
            (this.breakLabel, this.continueLabel, this.inExceptionBlock) = previous;
        }
    }

    internal void EmitNullableValue(LocalBuilder local, Type type, Type valueType, Type comparisonType)
    {
        if (Nullable.GetUnderlyingType(type) is not null)
        {
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Call, GetNullableAccessor(type, nameof(Nullable<int>.Value)));
        }
        else
            il.Emit(OpCodes.Ldloc, local);
        EmitConversion(valueType, comparisonType);
    }

    internal static MethodInfo GetNullableAccessor(Type nullableType, string name)
        => nullableType.GetProperty(name)?.GetMethod ?? throw new InvalidOperationException($"Nullable accessor {name} is missing on {nullableType}.");

    internal void EmitComparison(ExecutorComparison comparison, Type type)
    {
        if (type == typeof(string) && comparison is (ExecutorComparison.Equal or ExecutorComparison.NotEqual))
        {
            MethodInfo equals = typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)]) ?? throw new InvalidOperationException("string.Equals is missing.");
            il.Emit(OpCodes.Call, equals);
            if (comparison == ExecutorComparison.NotEqual)
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
            }
            return;
        }
        bool unsigned = IsUnsigned(type);
        bool floating = type == typeof(float) || type == typeof(double);
        OpCode instruction = comparison switch
        {
            ExecutorComparison.Equal or ExecutorComparison.NotEqual => OpCodes.Ceq,
            ExecutorComparison.LessThan => unsigned ? OpCodes.Clt_Un : OpCodes.Clt,
            ExecutorComparison.GreaterThan => unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt,
            ExecutorComparison.LessThanOrEqual => unsigned || floating ? OpCodes.Cgt_Un : OpCodes.Cgt,
            ExecutorComparison.GreaterThanOrEqual => unsigned || floating ? OpCodes.Clt_Un : OpCodes.Clt,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
        il.Emit(instruction);
        if (comparison is ExecutorComparison.NotEqual or ExecutorComparison.LessThanOrEqual or ExecutorComparison.GreaterThanOrEqual)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }
    }

    internal void EmitCompareZero(ExecutorComparison comparison)
    {
        il.Emit(OpCodes.Ldc_I4_0);
        EmitComparison(comparison, typeof(int));
    }

    internal static string GetDecimalBinaryMethod(ExecutorBinaryOperator @operator) => @operator switch
    {
        ExecutorBinaryOperator.Add => nameof(decimal.Add),
        ExecutorBinaryOperator.Subtract => nameof(decimal.Subtract),
        ExecutorBinaryOperator.Multiply => nameof(decimal.Multiply),
        ExecutorBinaryOperator.Divide => nameof(decimal.Divide),
        ExecutorBinaryOperator.Modulo => nameof(decimal.Remainder),
        _ => throw new InvalidOperationException($"Decimal operation {@operator} is not arithmetic.")
    };

    internal void EmitArguments(MethodBase method, IReadOnlyList<OperationArgument> arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != arguments.Count)
            throw new InvalidOperationException($"Method {method} has {parameters.Length} parameters but the plan supplies {arguments.Count}.");
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (parameter.ParameterType.IsByRef)
                EmitArgumentAddress(arguments[i], parameter.ParameterType.GetElementType()!);
            else
                EmitArgument(arguments[i], parameter.ParameterType);
        }
    }

    internal void EmitArgumentAddress(OperationArgument argument, Type expectedType)
    {
        Type actualType = argument.IsValue ? values[argument.Value].Type : argument.IsVariable ? locals[argument.Local].Type : argument.Type;
        if (actualType != expectedType)
            throw new InvalidOperationException($"A by-reference argument has type {actualType}, but the method requires {expectedType}.");
        if (argument.IsValue)
        {
            EmitAddress(values[argument.Value]);
            return;
        }
        if (argument.IsVariable)
        {
            EmitAddress(locals[argument.Local]);
            return;
        }
        throw new InvalidOperationException("A by-reference argument must be a variable.");
    }

    internal void EmitArgument(OperationArgument argument, Type expectedType)
    {
        if (argument.Kind == OperationArgumentKind.Value)
        {
            ValueLocation location = values[argument.Value];
            EmitLoad(location);
            EmitConversion(location.Type, expectedType);
            return;
        }
        if (argument.IsVariable)
        {
            ValueLocation location = locals[argument.Local];
            EmitLoad(location);
            EmitConversion(location.Type, expectedType);
            return;
        }

        if (argument.Data is null)
        {
            EmitNull(expectedType);
            return;
        }
        FieldBuilder field = constantField(argument);
        il.Emit(OpCodes.Ldsfld, field);
        EmitConversion(argument.Type, expectedType);
    }

    internal void EmitNull(Type targetType)
    {
        if (!targetType.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
            return;
        }
        if (Nullable.GetUnderlyingType(targetType) is null)
            throw new InvalidOperationException($"A null value cannot be emitted as {targetType}.");
        LocalBuilder local = il.DeclareLocal(targetType);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Initobj, targetType);
        il.Emit(OpCodes.Ldloc, local);
    }

    internal void EmitLoad(ValueLocation location)
    {
        if (location.Field is not null)
        {
            if (location.Field.IsStatic)
                il.Emit(OpCodes.Ldsfld, location.Field);
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, location.Field);
            }
            return;
        }
        if (location.Local is not null)
        {
            il.Emit(OpCodes.Ldloc, location.Local);
            return;
        }
        il.Emit(OpCodes.Ldarg, location.ArgumentIndex);
    }

    internal void EmitAddress(ValueLocation location)
    {
        if (location.Field is not null)
        {
            if (location.Field.IsStatic)
                il.Emit(OpCodes.Ldsflda, location.Field);
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldflda, location.Field);
            }
            return;
        }
        if (location.Local is not null)
        {
            il.Emit(OpCodes.Ldloca, location.Local);
            return;
        }
        il.Emit(OpCodes.Ldarga, location.ArgumentIndex);
    }

    internal Type EmitInstanceArgument(OperationArgument argument, Type dispatchType)
    {
        if (argument.IsValue)
            return EmitInstance(values[argument.Value], dispatchType);
        if (argument.IsVariable)
            return EmitInstance(locals[argument.Local], dispatchType);
        if (!argument.Type.IsValueType)
        {
            EmitArgument(argument, dispatchType);
            return dispatchType;
        }
        FieldBuilder field = constantField(argument);
        il.Emit(OpCodes.Ldsfld, field);
        LocalBuilder local = il.DeclareLocal(argument.Type);
        il.Emit(OpCodes.Stloc, local);
        return EmitInstance(new(argument.Type, local, 0), dispatchType);
    }

    internal Type EmitInstance(ValueLocation location, Type? dispatchType)
    {
        if (dispatchType is not null && !dispatchType.IsAssignableFrom(location.Type))
        {
            EmitLoad(location);
            EmitConversion(location.Type, dispatchType);
            if (dispatchType.IsValueType)
            {
                LocalBuilder adapted = il.DeclareLocal(dispatchType);
                il.Emit(OpCodes.Stloc, adapted);
                il.Emit(OpCodes.Ldloca, adapted);
            }
            return dispatchType;
        }
        if (location.Type.IsValueType && (dispatchType?.IsValueType == true || dispatchType?.IsInterface == true))
        {
            EmitAddress(location);
        }
        else
        {
            EmitLoad(location);
            if (location.Type.IsValueType)
                il.Emit(OpCodes.Box, location.Type);
        }
        return location.Type;
    }

    internal void EmitInvocation(MethodInfo method, Type? receiverType)
    {
        if (receiverType?.IsValueType == true && method.DeclaringType?.IsInterface == true)
            il.Emit(OpCodes.Constrained, receiverType);
        il.Emit(method.DeclaringType?.IsInterface == true || method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);
    }

    internal void EmitConversion(Type sourceType, Type targetType, bool isChecked = false)
    {
        if (sourceType == targetType)
            return;
        if (binding.RequiresStructuralBinding(targetType, sourceType))
            return;
        if (targetType.ContainsGenericParameters)
            throw new InvalidOperationException($"Generic target type {targetType} was not specialized before IL emission.");
        if (FindImplicitOperator(sourceType, targetType) is { Lifted: false } conversion)
        {
            EmitConversion(sourceType, conversion.Input, isChecked);
            il.Emit(OpCodes.Call, conversion.Method);
            EmitConversion(conversion.Output, targetType, isChecked);
            return;
        }
        Type? sourceNullable = Nullable.GetUnderlyingType(sourceType);
        Type? targetNullable = Nullable.GetUnderlyingType(targetType);
        if (sourceNullable is not null && targetNullable is not null)
        {
            EmitNullableConversion(sourceType, targetType, sourceNullable, targetNullable, isChecked);
            return;
        }
        if (targetNullable is not null && sourceNullable is null && sourceType.IsValueType)
        {
            EmitConversion(sourceType, targetNullable, isChecked);
            ConstructorInfo constructor = targetType.GetConstructor([targetNullable]) ?? throw new InvalidOperationException($"Nullable constructor for {targetType} is missing.");
            il.Emit(OpCodes.Newobj, constructor);
            return;
        }
        if (sourceNullable is not null && CanBoxNullableTo(sourceType, targetType))
        {
            il.Emit(OpCodes.Box, sourceType);
            if (targetType != typeof(object))
                il.Emit(OpCodes.Castclass, targetType);
            return;
        }
        if (targetType.IsAssignableFrom(sourceType))
        {
            EmitAssignableConversion(sourceType, targetType);
            return;
        }
        if (sourceNullable is not null && targetNullable is null)
        {
            MethodInfo value = sourceType.GetProperty(nameof(Nullable<int>.Value))?.GetMethod ?? throw new InvalidOperationException($"Nullable value accessor for {sourceType} is missing.");
            il.Emit(OpCodes.Call, value);
            EmitConversion(sourceNullable, targetType, isChecked);
            return;
        }
        if (sourceType.IsGenericParameter)
        {
            il.Emit(OpCodes.Box, sourceType);
            if (targetType != typeof(object))
                il.Emit(OpCodes.Castclass, targetType);
            return;
        }
        if (sourceType.IsValueType && !targetType.IsValueType)
        {
            il.Emit(OpCodes.Box, sourceType);
            il.Emit(OpCodes.Castclass, targetType);
            return;
        }
        if (IsNumeric(sourceType) && IsNumeric(targetType))
        {
            EmitNumericConversion(sourceType, targetType, isChecked);
            return;
        }
        if (sourceType.IsEnum || targetType.IsEnum)
        {
            Type sourceNumeric = sourceType.IsEnum ? Enum.GetUnderlyingType(sourceType) : sourceType;
            Type targetNumeric = targetType.IsEnum ? Enum.GetUnderlyingType(targetType) : targetType;
            if (IsNumeric(sourceNumeric) && IsNumeric(targetNumeric))
            {
                EmitNumericConversion(sourceNumeric, targetNumeric, isChecked);
                return;
            }
        }
        if (!sourceType.IsValueType)
        {
            il.Emit(targetType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, targetType);
            return;
        }
        throw new InvalidOperationException($"Conversion from {sourceType} to {targetType} is not supported.");
    }

    internal void EmitNullableConversion(Type sourceType, Type targetType, Type sourceUnderlying, Type targetUnderlying, bool isChecked)
    {
        LocalBuilder source = il.DeclareLocal(sourceType);
        il.Emit(OpCodes.Stloc, source);
        Label hasValue = il.DefineLabel();
        Label end = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, source);
        MethodInfo hasValueMethod = sourceType.GetProperty(nameof(Nullable<int>.HasValue))?.GetMethod ?? throw new InvalidOperationException($"Nullable HasValue accessor for {sourceType} is missing.");
        il.Emit(OpCodes.Call, hasValueMethod);
        il.Emit(OpCodes.Brtrue, hasValue);
        EmitNull(targetType);
        il.Emit(OpCodes.Br, end);
        il.MarkLabel(hasValue);
        il.Emit(OpCodes.Ldloca, source);
        MethodInfo valueMethod = sourceType.GetProperty(nameof(Nullable<int>.Value))?.GetMethod ?? throw new InvalidOperationException($"Nullable value accessor for {sourceType} is missing.");
        il.Emit(OpCodes.Call, valueMethod);
        EmitConversion(sourceUnderlying, targetUnderlying, isChecked);
        ConstructorInfo constructor = targetType.GetConstructor([targetUnderlying]) ?? throw new InvalidOperationException($"Nullable constructor for {targetType} is missing.");
        il.Emit(OpCodes.Newobj, constructor);
        il.MarkLabel(end);
    }

    internal void EmitNumericConversion(Type sourceType, Type targetType, bool isChecked = false)
    {
        Type source = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        bool unsignedSource = IsUnsigned(source);
        if (source == target)
            return;
        if (target == typeof(decimal) || source == typeof(decimal))
        {
            MethodInfo? conversion = typeof(decimal).GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => (x.Name == "op_Explicit" || target == typeof(decimal) && x.Name == "op_Implicit") && x.ReturnType == target && x.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == source);
            if (conversion is not null)
            {
                il.Emit(OpCodes.Call, conversion);
                return;
            }
        }
        if (target == typeof(sbyte))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_I1_Un : OpCodes.Conv_Ovf_I1 : OpCodes.Conv_I1);
        else if (target == typeof(byte))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_U1_Un : OpCodes.Conv_Ovf_U1 : OpCodes.Conv_U1);
        else if (target == typeof(char))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_U2_Un : OpCodes.Conv_Ovf_U2 : OpCodes.Conv_U2);
        else if (target == typeof(short))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_I2_Un : OpCodes.Conv_Ovf_I2 : OpCodes.Conv_I2);
        else if (target == typeof(ushort))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_U2_Un : OpCodes.Conv_Ovf_U2 : OpCodes.Conv_U2);
        else if (target == typeof(int))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_I4_Un : OpCodes.Conv_Ovf_I4 : OpCodes.Conv_I4);
        else if (target == typeof(uint))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_U4_Un : OpCodes.Conv_Ovf_U4 : OpCodes.Conv_U4);
        else if (target == typeof(long))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_I8_Un : OpCodes.Conv_Ovf_I8 : unsignedSource && source == typeof(uint) ? OpCodes.Conv_U8 : OpCodes.Conv_I8);
        else if (target == typeof(ulong))
            il.Emit(isChecked ? unsignedSource ? OpCodes.Conv_Ovf_U8_Un : OpCodes.Conv_Ovf_U8 : OpCodes.Conv_U8);
        else if (target == typeof(float))
        {
            if (unsignedSource)
                il.Emit(OpCodes.Conv_R_Un);
            il.Emit(OpCodes.Conv_R4);
        }
        else if (target == typeof(double))
        {
            if (unsignedSource)
                il.Emit(OpCodes.Conv_R_Un);
            il.Emit(OpCodes.Conv_R8);
        }
        else
            throw new InvalidOperationException($"Numeric conversion from {sourceType} to {targetType} is not supported.");
    }

    internal void EmitAssignableConversion(Type sourceType, Type targetType)
    {
        if (sourceType.IsValueType && !targetType.IsValueType)
            il.Emit(OpCodes.Box, sourceType);
        else if (!sourceType.IsValueType && targetType != typeof(object) && (targetType.IsInterface || !targetType.IsAssignableFrom(sourceType)))
            il.Emit(OpCodes.Castclass, targetType);
    }

    internal void StoreResult(PlanValue? result, Type actualType)
    {
        if (actualType == typeof(void))
            return;
        if (result is null)
        {
            il.Emit(OpCodes.Pop);
            return;
        }
        if (capturedFields.TryGetValue(result.Slot, out FieldBuilder? field))
        {
            ValueLocation captured = new(actualType, null, 0, field);
            EmitStore(captured);
            values[result] = captured;
        }
        else
        {
            LocalBuilder local = il.DeclareLocal(actualType);
            il.Emit(OpCodes.Stloc, local);
            values[result] = new(actualType, local, 0);
        }
    }

    internal void EmitStore(ValueLocation location)
    {
        if (location.Field is not null)
        {
            LocalBuilder value = il.DeclareLocal(location.Type);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stfld, location.Field);
        }
        else if (location.Local is not null)
        {
            il.Emit(OpCodes.Stloc, location.Local);
        }
        else
        {
            il.Emit(OpCodes.Starg, location.ArgumentIndex);
        }
    }

    internal void EmitDelegate(BoundDelegateOperation callback)
    {
        FieldBuilder field = delegateField(callback);
        foreach (var capture in callback.Captures)
        {
            if (capture.Variable is Variable variable && capturedFields.TryGetValue(variable.Slot, out var storage))
            {
                EmitArgument(capture, storage.FieldType);
                EmitStore(new(storage.FieldType, null, 0, storage));
            }
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        StoreResult(callback.Result, callback.DelegateType);
    }

    internal Type GetArgumentType(OperationArgument argument) => argument.Kind == OperationArgumentKind.Value ? values[argument.Value].Type : argument.IsVariable ? locals[argument.Local].Type : argument.Type;

    internal void EmitInstanceMethodCall(ValueLocation instance, MethodInfo method)
    {
        Type receiverType = EmitInstance(instance, method.DeclaringType);
        EmitInvocation(method, receiverType);
    }

}
