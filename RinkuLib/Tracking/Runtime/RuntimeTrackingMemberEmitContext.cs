using System;
using System.Reflection;
using System.Linq;
using System.Reflection.Emit;
using Rinku.Mapping;

namespace Rinku.Tracking.Runtime;

/// <summary>Provides emission context for one generated member.</summary>
public sealed class RuntimeTrackingMemberEmitContext {
    private static readonly MethodInfo DynaGet = typeof(DynaObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(x => x.Name == nameof(DynaObject.Get) && x.IsGenericMethodDefinition && x.GetParameters() is [{ ParameterType: var t }] && t == typeof(int));
    private static readonly MethodInfo DynaSet = typeof(DynaObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(x => x.Name == nameof(DynaObject.Set) && x.IsGenericMethodDefinition && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(int));

    internal RuntimeTrackingMemberEmitContext(RuntimeTrackingCapabilityBuilder builder, Type originalType, FieldBuilder originalField, FieldBuilder editField, MethodBuilder ensureEdit, int editIndex, string memberName) {
        Builder = builder;
        OriginalType = originalType;
        OriginalField = originalField;
        EditField = editField;
        EnsureEditMethod = ensureEdit;
        EditIndex = editIndex;
        MemberName = memberName;
    }

    /// <summary>Gets the capability builder.</summary>
    public RuntimeTrackingCapabilityBuilder Builder { get; }
    /// <summary>Gets the original type.</summary>
    public Type OriginalType { get; }
    /// <summary>Gets the original field.</summary>
    public FieldBuilder OriginalField { get; }
    /// <summary>Gets the edit field.</summary>
    public FieldBuilder EditField { get; }
    /// <summary>Gets the edit initializer method.</summary>
    public MethodBuilder EnsureEditMethod { get; }
    /// <summary>Gets the edit slot index.</summary>
    public int EditIndex { get; }
    /// <summary>Gets the member name.</summary>
    public string MemberName { get; }

    /// <summary>Emits a load of the original value.</summary>
    public void EmitLoadOriginal(ILGenerator il) {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OriginalType.IsValueType ? OpCodes.Ldflda : OpCodes.Ldfld, OriginalField);
    }

    /// <summary>Emits a tracked member read.</summary>
    public void EmitTrackedGet(ILGenerator il, Type valueType, Action<ILGenerator> emitBaseline) {
        if (EditIndex < 0) throw new InvalidOperationException($"Member '{MemberName}' has no edit slot.");
        Label baseline = il.DefineLabel();
        Label done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, EditField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, baseline);
        il.Emit(OpCodes.Ldc_I4, EditIndex);
        il.Emit(OpCodes.Callvirt, DynaGet.MakeGenericMethod(valueType));
        il.Emit(OpCodes.Br_S, done);
        il.MarkLabel(baseline);
        il.Emit(OpCodes.Pop);
        emitBaseline(il);
        il.MarkLabel(done);
    }

    /// <summary>Emits a tracked member write from the method argument.</summary>
    public void EmitTrackedSetFromArgument(ILGenerator il, Type valueType) {
        if (EditIndex < 0) throw new InvalidOperationException($"Member '{MemberName}' has no edit slot.");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EnsureEditMethod);
        il.Emit(OpCodes.Ldc_I4, EditIndex);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, DynaSet.MakeGenericMethod(valueType));
        il.Emit(OpCodes.Pop);
        EmitRaiseChanged(il);
    }

    /// <summary>Gets or creates a capability field.</summary>
    public FieldBuilder GetOrAddInstanceField(string key, Type fieldType, string? namePrefix = null)
        => Builder.GetOrAddInstanceField(key, fieldType, namePrefix);

    /// <summary>Emits a notification for this member.</summary>
    public void EmitRaiseChanged(ILGenerator il) => Builder.EmitRaiseChanged(il, MemberName);
}
