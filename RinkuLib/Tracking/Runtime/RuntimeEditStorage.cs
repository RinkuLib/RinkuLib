using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;

namespace Rinku.Tracking.Runtime;

/// <summary>Stores generated edit snapshots for one original type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IRuntimeEditStorage<TOriginal> {
    /// <summary>Creates an edit snapshot.</summary>
    DynaObject Create(TOriginal original, bool isNew);
    /// <summary>Resets an edit snapshot.</summary>
    void Reset(DynaObject edit, TOriginal original, bool isNew);
    /// <summary>Applies an edit snapshot.</summary>
    TOriginal Apply(DynaObject edit, TOriginal original);
    /// <summary>Returns whether an edit snapshot represents a new item.</summary>
    bool IsNew(DynaObject edit);
}

internal sealed class RuntimeDynaEditStorage<TOriginal> : IRuntimeEditStorage<TOriginal> {
    private delegate DynaObject CreateEdit(RuntimeDynaEditStorage<TOriginal> storage, TOriginal original, bool isNew);
    private delegate void ResetEdit(RuntimeDynaEditStorage<TOriginal> storage, DynaObject edit, TOriginal original, bool isNew);
    private delegate TOriginal ApplyEdit(RuntimeDynaEditStorage<TOriginal> storage, DynaObject edit, TOriginal original);

    private static readonly Type[] DynaTypes = [
        typeof(DynaObject), typeof(DynaObject<>), typeof(DynaObject<,>), typeof(DynaObject<,,>), typeof(DynaObject<,,,>),
        typeof(DynaObject<,,,,>), typeof(DynaObject<,,,,,>), typeof(DynaObject<,,,,,,>), typeof(DynaObject<,,,,,,,>),
        typeof(DynaObject<,,,,,,,,>), typeof(DynaObject<,,,,,,,,,>), typeof(DynaObject<,,,,,,,,,,>), typeof(DynaObject<,,,,,,,,,,,>)
    ];

    private static readonly MethodInfo DynaGet = typeof(DynaObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(x => x.Name == nameof(DynaObject.Get) && x.IsGenericMethodDefinition && x.GetParameters() is [{ ParameterType: var t }] && t == typeof(int));
    private static readonly MethodInfo DynaSet = typeof(DynaObject).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .Single(x => x.Name == nameof(DynaObject.Set) && x.IsGenericMethodDefinition && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(int));

    private readonly Mapper _mapper;
    private readonly CreateEdit _create;
    private readonly ResetEdit _reset;
    private readonly ApplyEdit _apply;

    public RuntimeDynaEditStorage(IReadOnlyList<IRuntimeTrackingMember> members) {
        IRuntimeEditableTrackingMember[] editable = members.OfType<IRuntimeEditableTrackingMember>().ToArray();
        string internalName = "RinkuTrackingInternalNewState";
        while (editable.Any(x => string.Equals(x.Name, internalName, StringComparison.OrdinalIgnoreCase))) internalName += "_";
        string[] names = new string[editable.Length + 1];
        names[0] = internalName;
        for (int i = 0; i < editable.Length; i++) names[i + 1] = editable[i].Name;
        _mapper = Mapper.GetMapper(names);
        _create = BuildCreate(editable);
        _reset = BuildReset(editable);
        _apply = BuildApply(editable);
    }

    public DynaObject Create(TOriginal original, bool isNew) => _create(this, original, isNew);
    public void Reset(DynaObject edit, TOriginal original, bool isNew) => _reset(this, edit, original, isNew);
    public TOriginal Apply(DynaObject edit, TOriginal original) => _apply(this, edit, original);
    public bool IsNew(DynaObject edit) => edit.Get<bool>(0);

    private CreateEdit BuildCreate(IRuntimeEditableTrackingMember[] editable) {
        Type[] slotTypes = new Type[editable.Length + 1];
        slotTypes[0] = typeof(bool);
        for (int i = 0; i < editable.Length; i++) slotTypes[i + 1] = editable[i].ValueType;

        var dm = new DynamicMethod($"TrackingCreateEdit_{typeof(TOriginal).Name}", typeof(DynaObject),
            [typeof(RuntimeDynaEditStorage<TOriginal>), typeof(TOriginal), typeof(bool)], typeof(DynaObject).Module, true);
        ILGenerator il = dm.GetILGenerator();
        int typedCount = Math.Min(12, slotTypes.Length);
        for (int slot = 0; slot < typedCount; slot++) EmitSlot(il, slot);

        Type dynaType;
        ConstructorInfo ctor;
        if (slotTypes.Length <= 12) {
            Type[] generic = slotTypes;
            dynaType = DynaTypes[slotTypes.Length].MakeGenericType(generic);
            ctor = dynaType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [.. generic, typeof(Mapper)], null)
                ?? throw new InvalidOperationException($"Unable to resolve {dynaType} constructor.");
        }
        else {
            int extraCount = slotTypes.Length - 12;
            il.Emit(OpCodes.Ldc_I4, extraCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int slot = 12; slot < slotTypes.Length; slot++) {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, slot - 12);
                EmitSlot(il, slot);
                if (slotTypes[slot].IsValueType) il.Emit(OpCodes.Box, slotTypes[slot]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            Type[] generic = slotTypes[..12];
            dynaType = typeof(DynaObjectInfinite<,,,,,,,,,,,>).MakeGenericType(generic);
            ctor = dynaType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [.. generic, typeof(object[]), typeof(Mapper)], null)
                ?? throw new InvalidOperationException($"Unable to resolve {dynaType} constructor.");
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeof(RuntimeDynaEditStorage<TOriginal>).GetField(nameof(_mapper), BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<CreateEdit>();

        void EmitSlot(ILGenerator emit, int slot) {
            if (slot == 0) { emit.Emit(OpCodes.Ldarg_2); return; }
            editable[slot - 1].EmitReadBaseline(emit, static e => {
                if (typeof(TOriginal).IsValueType) e.Emit(OpCodes.Ldarga_S, (byte)1);
                else
                    e.Emit(OpCodes.Ldarg_1);
            });
        }
    }

    private ResetEdit BuildReset(IRuntimeEditableTrackingMember[] editable) {
        var dm = new DynamicMethod($"TrackingResetEdit_{typeof(TOriginal).Name}", typeof(void),
            [typeof(RuntimeDynaEditStorage<TOriginal>), typeof(DynaObject), typeof(TOriginal), typeof(bool)], typeof(DynaObject).Module, true);
        ILGenerator il = dm.GetILGenerator();

        EmitSet(0, typeof(bool), static e => e.Emit(OpCodes.Ldarg_3));
        for (int i = 0; i < editable.Length; i++) {
            IRuntimeEditableTrackingMember member = editable[i];
            EmitSet(i + 1, member.ValueType, e => member.EmitReadBaseline(e, static load => {
                if (typeof(TOriginal).IsValueType) load.Emit(OpCodes.Ldarga_S, (byte)2);
                else
                    load.Emit(OpCodes.Ldarg_2);
            }));
        }
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<ResetEdit>();

        void EmitSet(int index, Type valueType, Action<ILGenerator> emitValue) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, index);
            emitValue(il);
            il.Emit(OpCodes.Callvirt, DynaSet.MakeGenericMethod(valueType));
            il.Emit(OpCodes.Pop);
        }
    }

    private ApplyEdit BuildApply(IRuntimeEditableTrackingMember[] editable) {
        var dm = new DynamicMethod($"TrackingApplyEdit_{typeof(TOriginal).Name}", typeof(TOriginal),
            [typeof(RuntimeDynaEditStorage<TOriginal>), typeof(DynaObject), typeof(TOriginal)], typeof(DynaObject).Module, true);
        ILGenerator il = dm.GetILGenerator();
        LocalBuilder original = il.DeclareLocal(typeof(TOriginal));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, original);

        for (int i = 0; i < editable.Length; i++) {
            IRuntimeEditableTrackingMember member = editable[i];
            int editIndex = i + 1;
            member.EmitApply(il,
                e => e.Emit(typeof(TOriginal).IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, original),
                e => {
                    e.Emit(OpCodes.Ldarg_1);
                    e.Emit(OpCodes.Ldc_I4, editIndex);
                    e.Emit(OpCodes.Callvirt, DynaGet.MakeGenericMethod(member.ValueType));
                });
        }

        il.Emit(OpCodes.Ldloc, original);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<ApplyEdit>();
    }
}
