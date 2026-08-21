using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Rinku.Tracking;

internal delegate bool TrackingNewStateGet<T>(ref T item);

internal static class TrackingListNewStateAccess<T>
{
    internal static readonly bool StaticSupported = typeof(ITrackingListNewState).IsAssignableFrom(typeof(T));
    internal static readonly bool RuntimeCapabilityPossible = StaticSupported || !typeof(T).IsValueType;

    private static readonly TrackingNewStateGet<T>? Getter = StaticSupported ? BuildGetter() : null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGet(ref T item, out bool isNew)
    {
        if (Getter is TrackingNewStateGet<T> getter)
        {
            isNew = getter(ref item);
            return true;
        }

        if (!typeof(T).IsValueType && item is ITrackingListNewState state)
        {
            isNew = state.IsNew;
            return true;
        }

        isNew = false;
        return false;
    }

    private static TrackingNewStateGet<T> BuildGetter()
    {
        MethodInfo method = typeof(ITrackingListNewState).GetProperty(nameof(ITrackingListNewState.IsNew))?.GetMethod
            ?? throw new MissingMethodException(typeof(ITrackingListNewState).FullName, $"get_{nameof(ITrackingListNewState.IsNew)}");

        var dynamicMethod = new DynamicMethod(
            $"TrackingList_{typeof(T).Name}_GetIsNew",
            typeof(bool),
            [typeof(T).MakeByRefType()],
            typeof(TrackingListNewStateAccess<T>).Module,
            true);

        ILGenerator il = dynamicMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (typeof(T).IsValueType)
            il.Emit(OpCodes.Constrained, typeof(T));
        else
            il.Emit(OpCodes.Ldind_Ref);
        il.Emit(OpCodes.Callvirt, method);
        il.Emit(OpCodes.Ret);
        return dynamicMethod.CreateDelegate<TrackingNewStateGet<T>>();
    }
}
