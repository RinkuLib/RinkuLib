using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking;

internal sealed class DefaultTrackingListContext<T> : ITrackingListContext<T>
{
    internal static readonly DefaultTrackingListContext<T> Instance = new();
    private static readonly Func<T>? Factory = BuildFactory();

    private DefaultTrackingListContext() { }

    public bool CanCreateNew => Factory is not null;

    public T CreateNew()
        => Factory is Func<T> factory
            ? factory()
            : throw new NotSupportedException($"{typeof(T)} has no public parameterless constructor and no tracking-list context factory was supplied.");

    public bool ConfirmAdded(T item) => true;
    public bool ConfirmEdit(T item) => true;
    public bool ConfirmDelete(T item) => true;

    private static Func<T>? BuildFactory()
    {
        Type type = typeof(T);
        if (type.IsAbstract || type.IsInterface) return null;

        var method = new DynamicMethod($"TrackingList_Create_{type.Name}", type, Type.EmptyTypes, typeof(DefaultTrackingListContext<T>).Module, true);
        ILGenerator il = method.GetILGenerator();

        if (type.IsValueType)
        {
            LocalBuilder value = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<T>>();
        }

        ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null) return null;

        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<T>>();
    }
}
