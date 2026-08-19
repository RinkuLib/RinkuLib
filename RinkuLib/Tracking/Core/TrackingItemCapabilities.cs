using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Rinku.Tracking;

// One-time capability specialization per T. This keeps TrackingList<T> free of per-item reflection
// and avoids boxing value-type capability implementations on hot paths.
internal static class TrackingItemCapabilities<T> {
    internal static readonly bool HasOriginalCapability = typeof(IHasOriginal).IsAssignableFrom(typeof(T));
    internal static readonly bool IsEditable = typeof(IEditable).IsAssignableFrom(typeof(T));

    private static readonly Func<T, bool>? HasOriginalCall = HasOriginalCapability ? CreateBoolPropertyCall(typeof(IHasOriginal), nameof(IHasOriginal.HasOriginal)) : null;
    private static readonly Func<T, bool>? IsEditingCall = IsEditable ? CreateBoolPropertyCall(typeof(IEditable), nameof(IEditable.IsEditing)) : null;
    private static readonly Func<T, bool>? CommitEditCall = IsEditable ? CreateBoolMethodCall(typeof(IEditable), nameof(IEditable.CommitEdit)) : null;
    private static readonly Func<T, bool>? CancelEditCall = IsEditable ? CreateBoolMethodCall(typeof(IEditable), nameof(IEditable.CancelEdit)) : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasOriginal(T item) {
        if (!HasOriginalCapability) return false;
        if (!typeof(T).IsValueType && item is null) return false;
        return HasOriginalCall!(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsEditing(T item) {
        if (!IsEditable) return false;
        if (!typeof(T).IsValueType && item is null) return false;
        return IsEditingCall!(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool CommitEdit(T item) {
        if (!IsEditable) return false;
        if (!typeof(T).IsValueType && item is null) return false;
        return CommitEditCall!(item);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool CancelEdit(T item) {
        if (!IsEditable) return false;
        if (!typeof(T).IsValueType && item is null) return false;
        return CancelEditCall!(item);
    }

    private static Func<T, bool> CreateBoolPropertyCall(Type contract, string propertyName) {
        MethodInfo getter = contract.GetProperty(propertyName)!.GetMethod!;
        return CreateBoolCall(getter, propertyName);
    }

    private static Func<T, bool> CreateBoolMethodCall(Type contract, string methodName) {
        MethodInfo method = contract.GetMethod(methodName, Type.EmptyTypes)!;
        return CreateBoolCall(method, methodName);
    }

    private static Func<T, bool> CreateBoolCall(MethodInfo method, string name) {
        var dm = new DynamicMethod($"Tracking_{typeof(T).Name}_{name}", typeof(bool), [typeof(T)], typeof(TrackingItemCapabilities<T>).Module, true);
        ILGenerator il = dm.GetILGenerator();
        if (typeof(T).IsValueType) {
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Constrained, typeof(T));
            il.Emit(OpCodes.Callvirt, method);
        }
        else {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, method);
        }
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Func<T, bool>>();
    }
}
