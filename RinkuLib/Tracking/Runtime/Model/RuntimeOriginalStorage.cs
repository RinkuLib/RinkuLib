using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Expandable original-storage policy selected during the configuration phase.</summary>
public interface IRuntimeOriginalStorageEmitter<TOriginal>
{
    /// <summary>Defines the original value field.</summary>
    FieldBuilder DefineField(TypeBuilder type);
    /// <summary>Emits storage from a method argument.</summary>
    void EmitStoreFromArgument(ILGenerator il, FieldBuilder field, int argument);
    /// <summary>Emits a load of the original value.</summary>
    void EmitLoadValue(ILGenerator il, FieldBuilder field);
    /// <summary>Emits a load of the original target.</summary>
    void EmitLoadTarget(ILGenerator il, FieldBuilder field);
    /// <summary>Emits typed original access.</summary>
    void EmitTryGetOriginal(ILGenerator il, FieldBuilder field);
}

/// <summary>Stores an original value that is always available.</summary>
public sealed class RuntimeRequiredOriginalStorage<TOriginal> : IRuntimeOriginalStorageEmitter<TOriginal>
{
    /// <summary>Gets the shared storage emitter.</summary>
    public static RuntimeRequiredOriginalStorage<TOriginal> Instance { get; } = new();
    private RuntimeRequiredOriginalStorage() { }

    /// <inheritdoc/>
    public FieldBuilder DefineField(TypeBuilder type)
        => type.DefineField("_original", typeof(TOriginal), FieldAttributes.Private);

    /// <inheritdoc/>
    public void EmitStoreFromArgument(ILGenerator il, FieldBuilder field, int argument)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(argument switch { 1 => OpCodes.Ldarg_1, 2 => OpCodes.Ldarg_2, 3 => OpCodes.Ldarg_3, _ => throw new ArgumentOutOfRangeException(nameof(argument)) });
        il.Emit(OpCodes.Stfld, field);
    }

    /// <inheritdoc/>
    public void EmitLoadValue(ILGenerator il, FieldBuilder field)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
    }

    /// <inheritdoc/>
    public void EmitLoadTarget(ILGenerator il, FieldBuilder field)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(typeof(TOriginal).IsValueType ? OpCodes.Ldflda : OpCodes.Ldfld, field);
    }

    /// <inheritdoc/>
    public void EmitTryGetOriginal(ILGenerator il, FieldBuilder field)
    {
        il.Emit(OpCodes.Ldarg_1);
        EmitLoadValue(il, field);
        EmitStoreIndirect(il, typeof(TOriginal));
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    internal static void EmitStoreIndirect(ILGenerator il, Type type)
    {
        if (type.IsValueType) il.Emit(OpCodes.Stobj, type);
        else il.Emit(OpCodes.Stind_Ref);
    }
}

/// <summary>
/// Optional reference-original policy where null means "original not available". Use a custom storage emitter when null itself must be a valid available original.
/// </summary>
public sealed class RuntimeNullOriginalStorage<TOriginal> : IRuntimeOriginalStorageEmitter<TOriginal>
{
    public RuntimeNullOriginalStorage()
    {
        if (typeof(TOriginal).IsValueType)
            throw new InvalidOperationException($"{nameof(RuntimeNullOriginalStorage<TOriginal>)} requires a reference TOriginal.");
    }

    /// <inheritdoc/>
    public FieldBuilder DefineField(TypeBuilder type)
        => type.DefineField("_original", typeof(TOriginal), FieldAttributes.Private);

    /// <inheritdoc/>
    public void EmitStoreFromArgument(ILGenerator il, FieldBuilder field, int argument)
        => RuntimeRequiredOriginalStorage<TOriginal>.Instance.EmitStoreFromArgument(il, field, argument);

    /// <inheritdoc/>
    public void EmitLoadValue(ILGenerator il, FieldBuilder field)
        => RuntimeRequiredOriginalStorage<TOriginal>.Instance.EmitLoadValue(il, field);

    /// <inheritdoc/>
    public void EmitLoadTarget(ILGenerator il, FieldBuilder field)
        => RuntimeRequiredOriginalStorage<TOriginal>.Instance.EmitLoadTarget(il, field);

    /// <inheritdoc/>
    public void EmitTryGetOriginal(ILGenerator il, FieldBuilder field)
    {
        Label unavailable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Brfalse, unavailable);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(unavailable);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
