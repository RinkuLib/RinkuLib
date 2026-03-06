using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.DbRegister;
/// <inheritdoc/>
public delegate TItem Getter<TObj, TItem>(TObj instance);
/// <inheritdoc/>
public delegate void Setter<TObj, TItem>(TObj instance, TItem value);

/// <inheritdoc/>
public delegate TItem StructGetter<TObj, TItem>(ref TObj instance);
/// <inheritdoc/>
public delegate void StructSetter<TObj, TItem>(ref TObj instance, TItem value);
/// <inheritdoc/>
public static class AccessorFactory {
    /// <inheritdoc/>
    public static Delegate CreateGetter(Type objType, Type itemType, MemberInfo member) {
        bool isStruct = objType.IsValueType;
        Type delegateType = isStruct
            ? typeof(StructGetter<,>).MakeGenericType(objType, itemType)
            : typeof(Getter<,>).MakeGenericType(objType, itemType);

        if (member is PropertyInfo prop && prop.CanRead && prop.GetMethod is not null)
            return prop.GetMethod.CreateDelegate(delegateType);

        if (member is MethodInfo method)
            return method.CreateDelegate(delegateType);

        if (member is FieldInfo field) {
            Type targetType = isStruct ? objType.MakeByRefType() : objType;
            var dm = new DynamicMethod("GetField", itemType, [targetType], objType.Module, true);
            var il = dm.GetILGenerator();

            if (field.IsStatic) {
                il.Emit(OpCodes.Ldsfld, field);
            }
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
            }
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(delegateType);
        }

        throw new NotSupportedException($"Cannot create getter for {member.Name}");
    }

    /// <inheritdoc/>
    public static Delegate CreateSetter(Type objType, Type itemType, MemberInfo member) {
        bool isStruct = objType.IsValueType;
        Type delegateType = isStruct
            ? typeof(StructSetter<,>).MakeGenericType(objType, itemType)
            : typeof(Setter<,>).MakeGenericType(objType, itemType);

        if (member is PropertyInfo prop && prop.CanWrite && prop.SetMethod is not null)
            return prop.SetMethod.CreateDelegate(delegateType);

        if (member is MethodInfo method)
            return method.CreateDelegate(delegateType);

        if (member is FieldInfo field) {
            Type targetType = isStruct ? objType.MakeByRefType() : objType;
            var dm = new DynamicMethod("SetField", null, [targetType, itemType], objType.Module, true);
            var il = dm.GetILGenerator();

            if (field.IsStatic) {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stsfld, field);
            }
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate(delegateType);
        }

        throw new NotSupportedException($"Cannot create setter for {member.Name}");
    }
}