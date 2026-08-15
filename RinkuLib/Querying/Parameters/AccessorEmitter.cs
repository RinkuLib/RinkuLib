using System.Reflection;
using System.Reflection.Emit;
using System.Data;
using Rinku.Querying;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

/// <summary>Emits one parameter member through both parameter-object execution paths.</summary>
public interface IAccessorEmitter {
    /// <summary>Emits the direct parameter binding path.</summary>
    void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue);

    /// <summary>Emits the value-array path used by <c>UseWith</c>.</summary>
    void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue);

    /// <summary>Checks the emitter against the member it will read.</summary>
    void Validate(Type type, MemberInfo member);
}

/// <summary>Emits one parameter key whose rule belongs to the whole parameter type.</summary>
public interface ITypeAccessorEmitter {
    /// <summary>Emits the direct parameter binding path.</summary>
    void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues,
        int handlerIndex, bool handlerValue, bool bindValue);
    /// <summary>Emits the value-array path used by <c>UseWith</c>.</summary>
    void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue);
    /// <summary>Checks the emitter against the parameter type.</summary>
    void Validate(Type type);
}

/// <summary>Provides the common conditional flow for an accessor emitter.</summary>
public abstract class AccessorEmitterBase : IAccessorEmitter {
    /// <inheritdoc/>
    public virtual void Validate(Type type, MemberInfo member) { }

    /// <summary>Emits the condition that decides whether this key is usable.</summary>
    protected abstract void EmitCondition(ILGenerator il, Type type, MemberInfo member);

    /// <summary>Emits the value read when the key is usable.</summary>
    protected abstract void EmitValue(ILGenerator il, Type type, MemberInfo member);

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex,
            handlerValue, bindValue,
            x => EmitCondition(x, type, member),
            x => EmitValue(x, type, member));
    }

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue) {
        AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
            x => EmitCondition(x, type, member),
            x => EmitValue(x, type, member));
    }
}

/// <summary>Provides the common conditional flow for an emitter attached to a whole type.</summary>
public abstract class TypeAccessorEmitterBase : ITypeAccessorEmitter {
    /// <inheritdoc/>
    public virtual void Validate(Type type) { }

    /// <summary>Emits the condition that decides whether this key is usable.</summary>
    protected abstract void EmitCondition(ILGenerator il, Type type);
    /// <summary>Emits the value read when the key is usable.</summary>
    protected abstract void EmitValue(ILGenerator il, Type type);

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues,
        int handlerIndex, bool handlerValue, bool bindValue) {
        AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex,
            handlerValue, bindValue,
            x => EmitCondition(x, type),
            x => EmitValue(x, type));
    }

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue) {
        AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
            x => EmitCondition(x, type),
            x => EmitValue(x, type));
    }
}

/// <summary>Uses a static boolean method over the member value as the usability condition.</summary>
public class MethodConditionEmitter : AccessorEmitterBase {
    private readonly MethodInfo _method;
    private readonly bool _invert;

    /// <summary>Creates a condition emitter for a static method with one argument and a boolean result.</summary>
    public MethodConditionEmitter(MethodInfo method, bool invert = false) {
        var parameters = method.GetParameters();
        if (!method.IsStatic || method.ReturnType != typeof(bool) || parameters.Length != 1 || parameters[0].ParameterType.IsByRef)
            throw new ArgumentException("The condition method must be static, return bool, and take one parameter.", nameof(method));
        _method = method;
        _invert = invert;
    }

    /// <inheritdoc/>
    public override void Validate(Type type, MemberInfo member) {
        var memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        var methodType = _method.GetParameters()[0].ParameterType;
        if (!methodType.IsAssignableFrom(memberType))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType,
                $"The condition method { _method.Name } accepts {methodType}, but the member {member.Name} is {memberType}.");
    }

    /// <inheritdoc/>
    protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member) {
        AccessorEmitter.EmitMemberValue(il, type, member);
        il.Emit(OpCodes.Call, _method);
        if (_invert) {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }
    }

    /// <inheritdoc/>
    protected override void EmitValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberValue(il, type, member);
}

/// <summary>
/// The base for an attribute that changes how a parameter object's member is read, its presence rule, its
/// value, or both. Attributes like <see cref="ForBoolCondAttribute"/> and
/// <see cref="NotNullOrWhitespaceAttribute"/>, subclass it to define a custom rule of your own.
/// </summary>
public abstract class AccessorEmitterHandler : Attribute {
    /// <summary>Returns an emitter for the whole parameter type and requested key.</summary>
    public virtual ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper) => null;

    /// <summary>Returns an emitter for a parameter member and requested key.</summary>
    public virtual IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) => null;
}
/// <summary>Shared IL helpers for member access.</summary>
public static class AccessorEmitter {
    /// <summary>Emits one direct parameter binding slot.</summary>
    public static void EmitSlot(ILGenerator il, int index, string key,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue,
        Action<ILGenerator> condition, Action<ILGenerator> value) {
        var skip = il.DefineLabel();
        condition(il);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Call, typeof(Span<bool>).GetProperty("Item")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I1);
        if (!bindValue) { }
        else if (handlerValue) {
            il.Emit(OpCodes.Ldloc, handlerValues!);
            il.Emit(OpCodes.Ldc_I4, handlerIndex);
            value(il);
            il.Emit(OpCodes.Stelem_Ref);
        }
        else {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldarg_1);
            value(il);
            il.Emit(OpCodes.Callvirt, typeof(DbParamInfo).GetMethod(nameof(DbParamInfo.Use), [typeof(string), typeof(IDbCommand), typeof(object)])!);
            il.Emit(OpCodes.Pop);
        }
        il.MarkLabel(skip);
    }

    /// <summary>Emits one value-array binding slot.</summary>
    public static void EmitUseWithSlot(ILGenerator il, int index, bool bindValue,
        Action<ILGenerator> condition, Action<ILGenerator> value) {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        var skip = il.DefineLabel();
        condition(il);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        if (bindValue) value(il);
        else il.Emit(OpCodes.Ldsfld, typeof(AccessorUsageMarker).GetField(nameof(AccessorUsageMarker.Value), BindingFlags.Static | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Stelem_Ref);
        il.MarkLabel(skip);
    }

    /// <summary>
    /// Helper to load the instance and access the specific member.
    /// Handles the difference between ref structs (void*) and class references.
    /// </summary>
    public static void EmitMemberLoad(ILGenerator il, Type targetType, MemberInfo member) {
        if (member is FieldInfo field) {
            if (field.IsStatic)
                il.Emit(OpCodes.Ldsfld, field);
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
            }
        }
        else {
            var meth = member as MethodInfo;
            if (meth is null) {
                if (member is PropertyInfo prop)
                    meth = prop.GetMethod!;
                if (meth is null)
                    throw new RinkuConfigurationException(ErrorCodes.UnusableMember, "The member must be a field, property or method");
            }
            if (meth.IsStatic)
                il.Emit(OpCodes.Call, meth);
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(targetType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, meth);
            }
        }
    }

    /// <summary>Loads a member value and boxes it when the member type is a value type.</summary>
    public static void EmitMemberValue(ILGenerator il, Type targetType, MemberInfo member) {
        EmitMemberLoad(il, targetType, member);
        var memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (memberType.IsValueType)
            il.Emit(OpCodes.Box, memberType);
    }
}
