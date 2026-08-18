using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;
using Rinku.Querying;

namespace Rinku.Querying.Parameters;

/// <summary>Emits one direct parameter member through both parameter-object execution paths.</summary>
public interface IAccessorEmitter {
    /// <summary>Emits the direct parameter binding path.</summary>
    void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue);

    /// <summary>Emits the value-array path used by <c>UseWith</c>.</summary>
    void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue);

    /// <summary>Emits whether the member is currently usable, leaving a <see cref="bool"/> on the IL stack.</summary>
    void EmitStackUsage(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitDefaultStackUsage(il, type, member);

    /// <summary>Emits the raw typed member value directly onto the IL stack.</summary>
    void EmitStackValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberLoad(il, type, member);

    /// <summary>The raw type left on the IL stack by <see cref="EmitStackValue"/>.</summary>
    Type GetStackType(Type type, MemberInfo member)
        => ParameterMemberAccess.GetMemberType(member);

    /// <summary>Checks the emitter against the member it will read.</summary>
    void Validate(Type type, MemberInfo member);
}

/// <summary>
/// Optional path-aware form of <see cref="IAccessorEmitter"/>. Implement this when the same custom parameter
/// rule should also work on members reached through <see cref="NestedParametersAttribute"/>.
/// </summary>
public interface IPathAccessorEmitter {
    /// <summary>Emits the direct parameter binding path.</summary>
    void Emit(ILGenerator il, int index, string key, ParameterMemberAccess member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue);

    /// <summary>Emits the value-array path used by <c>UseWith</c>.</summary>
    void EmitUseWith(ILGenerator il, int index, ParameterMemberAccess member, bool bindValue);

    /// <summary>Emits whether the resolved value is currently usable, leaving a <see cref="bool"/> on the IL stack.</summary>
    void EmitStackUsage(ILGenerator il, ParameterMemberAccess member);

    /// <summary>Emits the raw typed resolved value directly onto the IL stack.</summary>
    void EmitStackValue(ILGenerator il, ParameterMemberAccess member);

    /// <summary>The raw type left on the IL stack by <see cref="EmitStackValue"/>.</summary>
    Type GetStackType(ParameterMemberAccess member);

    /// <summary>Checks the emitter against the resolved member path.</summary>
    void Validate(ParameterMemberAccess member);
}

/// <summary>Emits one parameter key whose rule belongs to the whole parameter type.</summary>
public interface ITypeAccessorEmitter {
    /// <summary>Emits the direct parameter binding path.</summary>
    void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues,
        int handlerIndex, bool handlerValue, bool bindValue);
    /// <summary>Emits the value-array path used by <c>UseWith</c>.</summary>
    void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue);
    /// <summary>Emits whether the type-level value is currently usable, leaving a <see cref="bool"/> on the IL stack.</summary>
    void EmitStackUsage(ILGenerator il, Type type);
    /// <summary>Emits the raw typed type-level value directly onto the IL stack.</summary>
    void EmitStackValue(ILGenerator il, Type type);
    /// <summary>The raw type left on the IL stack by <see cref="EmitStackValue"/>.</summary>
    Type GetStackType(Type type);
    /// <summary>Checks the emitter against the parameter type.</summary>
    void Validate(Type type);
}

/// <summary>Provides the common conditional flow for a direct-member accessor emitter.</summary>
public abstract class AccessorEmitterBase : IAccessorEmitter {
    /// <inheritdoc/>
    public virtual void Validate(Type type, MemberInfo member) { }

    /// <summary>Emits the condition that decides whether this key is usable.</summary>
    protected abstract void EmitCondition(ILGenerator il, Type type, MemberInfo member);

    /// <summary>Emits the raw typed source value.</summary>
    protected abstract void EmitValue(ILGenerator il, Type type, MemberInfo member);

    /// <summary>The raw type emitted by <see cref="EmitValue"/>.</summary>
    protected virtual Type GetValueType(Type type, MemberInfo member) => ParameterMemberAccess.GetMemberType(member);

    /// <summary>Emits the object value consumed by command binding and <c>UseWith</c>.</summary>
    protected virtual void EmitParameterValue(ILGenerator il, Type type, MemberInfo member) {
        EmitValue(il, type, member);
    }

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex,
            handlerValue, bindValue,
            x => EmitCondition(x, type, member),
            x => EmitParameterValue(x, type, member));
    }

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue) {
        AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
            x => EmitCondition(x, type, member),
            x => EmitParameterValue(x, type, member));
    }

    /// <inheritdoc/>
    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member) => EmitCondition(il, type, member);

    /// <inheritdoc/>
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member) => EmitValue(il, type, member);

    /// <inheritdoc/>
    public Type GetStackType(Type type, MemberInfo member) => GetValueType(type, member);
}

/// <summary>
/// Provides the common conditional flow for a member rule that supports both direct and flattened/nested
/// parameter members. Prefer this base for new custom emitters.
/// </summary>
public abstract class PathAccessorEmitterBase : IAccessorEmitter, IPathAccessorEmitter {
    /// <inheritdoc/>
    public virtual void Validate(ParameterMemberAccess member) { }

    /// <summary>Emits the condition that decides whether this key is usable.</summary>
    protected abstract void EmitCondition(ILGenerator il, ParameterMemberAccess member);

    /// <summary>Emits the raw typed source value.</summary>
    protected abstract void EmitValue(ILGenerator il, ParameterMemberAccess member);

    /// <summary>The raw type emitted by <see cref="EmitValue"/>.</summary>
    protected virtual Type GetValueType(ParameterMemberAccess member) => member.MemberType;

    /// <summary>Emits the object value consumed by command binding and <c>UseWith</c>.</summary>
    protected virtual void EmitParameterValue(ILGenerator il, ParameterMemberAccess member) {
        EmitValue(il, member);
        AccessorEmitter.BoxIfNeeded(il, GetValueType(member));
    }

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, ParameterMemberAccess member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        AccessorEmitter.EmitSlot(il, index, key, member, handlerValues, handlerIndex,
            handlerValue, bindValue,
            static (self, x, m) => self.EmitCondition(x, m),
            static (self, x, m) => self.EmitParameterValue(x, m), this);
    }

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, ParameterMemberAccess member, bool bindValue) {
        AccessorEmitter.EmitUseWithSlot(il, index, bindValue, member,
            static (self, x, m) => self.EmitCondition(x, m),
            static (self, x, m) => self.EmitParameterValue(x, m), this);
    }

    /// <inheritdoc/>
    public void EmitStackUsage(ILGenerator il, ParameterMemberAccess member) => EmitCondition(il, member);

    /// <inheritdoc/>
    public void EmitStackValue(ILGenerator il, ParameterMemberAccess member) => EmitValue(il, member);

    /// <inheritdoc/>
    public Type GetStackType(ParameterMemberAccess member) => GetValueType(member);

    /// <inheritdoc/>
    public void Validate(Type type, MemberInfo member)
        => Validate(new ParameterMemberAccess(type, [member]));

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => Emit(il, index, key, new ParameterMemberAccess(type, [member]), handlerValues,
            handlerIndex, handlerValue, bindValue);

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue)
        => EmitUseWith(il, index, new ParameterMemberAccess(type, [member]), bindValue);

    /// <inheritdoc/>
    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member)
        => EmitStackUsage(il, new ParameterMemberAccess(type, [member]));

    /// <inheritdoc/>
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member)
        => EmitStackValue(il, new ParameterMemberAccess(type, [member]));

    /// <inheritdoc/>
    public Type GetStackType(Type type, MemberInfo member)
        => GetStackType(new ParameterMemberAccess(type, [member]));
}

/// <summary>Provides the common conditional flow for an emitter attached to a whole type.</summary>
public abstract class TypeAccessorEmitterBase : ITypeAccessorEmitter {
    /// <inheritdoc/>
    public virtual void Validate(Type type) { }

    /// <summary>Emits the condition that decides whether this key is usable.</summary>
    protected abstract void EmitCondition(ILGenerator il, Type type);
    /// <summary>Emits the raw typed source value.</summary>
    protected abstract void EmitValue(ILGenerator il, Type type);
    /// <summary>The raw type emitted by <see cref="EmitValue"/>.</summary>
    protected virtual Type GetValueType(Type type) => type;

    /// <summary>Emits the object value consumed by command binding and <c>UseWith</c>.</summary>
    protected virtual void EmitParameterValue(ILGenerator il, Type type) {
        EmitValue(il, type);
    }

    /// <inheritdoc/>
    public void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues,
        int handlerIndex, bool handlerValue, bool bindValue) {
        AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex,
            handlerValue, bindValue,
            x => EmitCondition(x, type),
            x => EmitParameterValue(x, type));
    }

    /// <inheritdoc/>
    public void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue) {
        AccessorEmitter.EmitUseWithSlot(il, index, bindValue,
            x => EmitCondition(x, type),
            x => EmitParameterValue(x, type));
    }

    /// <inheritdoc/>
    public void EmitStackUsage(ILGenerator il, Type type) => EmitCondition(il, type);
    /// <inheritdoc/>
    public void EmitStackValue(ILGenerator il, Type type) => EmitValue(il, type);
    /// <inheritdoc/>
    public Type GetStackType(Type type) => GetValueType(type);
}

/// <summary>Uses a static boolean method over the member value as the usability condition.</summary>
public class MethodConditionEmitter : PathAccessorEmitterBase {
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
    public override void Validate(ParameterMemberAccess member) {
        Type methodType = _method.GetParameters()[0].ParameterType;
        if (!methodType.IsAssignableFrom(member.MemberType))
            throw new RinkuConfigurationException(ErrorCodes.AttributeOnWrongMemberType,
                $"The condition method {_method.Name} accepts {methodType}, but the member {member.Member.Name} is {member.MemberType}.");
    }

    /// <inheritdoc/>
    protected override void EmitCondition(ILGenerator il, ParameterMemberAccess member) {
        member.EmitLoad(il);
        il.Emit(OpCodes.Call, _method);
        if (_invert) {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }
    }

    /// <inheritdoc/>
    protected override void EmitValue(ILGenerator il, ParameterMemberAccess member)
        => member.EmitLoad(il);
}

/// <summary>
/// The base for an attribute that changes how a parameter object's member is read, its presence rule, its
/// value, or both. Attributes like <see cref="ForBoolCondAttribute"/> and
/// <see cref="NotNullOrWhitespaceAttribute"/> subclass it to define a custom rule of your own.
/// </summary>
public abstract class AccessorEmitterHandler : Attribute {
    /// <summary>Returns an emitter for the whole parameter type and requested key.</summary>
    public virtual ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper) => null;

    /// <summary>Returns an emitter for a parameter member and requested key.</summary>
    public virtual IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) => null;
}

/// <summary>Shared IL helpers for parameter access.</summary>
public static class AccessorEmitter {
    private static readonly MethodInfo SpanItem = typeof(Span<bool>).GetProperty("Item")!.GetMethod!;
    private static readonly MethodInfo DbParamUse = typeof(DbParamInfo).GetMethod(
        nameof(DbParamInfo.Use), [typeof(string), typeof(IDbCommand), typeof(object)])!;
    private static readonly FieldInfo UsageMarker = typeof(AccessorUsageMarker).GetField(
        nameof(AccessorUsageMarker.Value), BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>Emits one direct parameter binding slot.</summary>
    public static void EmitSlot(ILGenerator il, int index, string key,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue,
        Action<ILGenerator> condition, Action<ILGenerator> value) {
        var skip = il.DefineLabel();
        condition(il);
        il.Emit(OpCodes.Brfalse, skip);
        EmitDirectValue(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue, value);
        il.MarkLabel(skip);
    }

    // Emits one nested/path-aware direct parameter binding slot.
    internal static void EmitSlot(ILGenerator il, int index, string key, ParameterMemberAccess member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue,
        Action<ILGenerator, ParameterMemberAccess> condition,
        Action<ILGenerator, ParameterMemberAccess> value) {
        var skip = il.DefineLabel();
        ParameterMemberAccess prepared = member.Prepare(il, skip);
        condition(il, prepared);
        il.Emit(OpCodes.Brfalse, skip);
        EmitDirectValue(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue,
            x => value(x, prepared));
        il.MarkLabel(skip);
    }

    // Emits one nested/path-aware direct parameter binding slot without closure allocations in emitter bases.
    internal static void EmitSlot<TEmitter>(ILGenerator il, int index, string key, ParameterMemberAccess member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue,
        Action<TEmitter, ILGenerator, ParameterMemberAccess> condition,
        Action<TEmitter, ILGenerator, ParameterMemberAccess> value, TEmitter emitter) {
        var skip = il.DefineLabel();
        ParameterMemberAccess prepared = member.Prepare(il, skip);
        condition(emitter, il, prepared);
        il.Emit(OpCodes.Brfalse, skip);
        EmitDirectValue(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue,
            x => value(emitter, x, prepared));
        il.MarkLabel(skip);
    }

    /// <summary>Emits one value-array binding slot.</summary>
    public static void EmitUseWithSlot(ILGenerator il, int index, bool bindValue,
        Action<ILGenerator> condition, Action<ILGenerator> value) {
        ClearUseWithSlot(il, index);
        var skip = il.DefineLabel();
        condition(il);
        il.Emit(OpCodes.Brfalse, skip);
        EmitUseWithValue(il, index, bindValue, value);
        il.MarkLabel(skip);
    }

    // Emits one nested/path-aware value-array binding slot.
    internal static void EmitUseWithSlot(ILGenerator il, int index, bool bindValue, ParameterMemberAccess member,
        Action<ILGenerator, ParameterMemberAccess> condition,
        Action<ILGenerator, ParameterMemberAccess> value) {
        ClearUseWithSlot(il, index);
        var skip = il.DefineLabel();
        ParameterMemberAccess prepared = member.Prepare(il, skip);
        condition(il, prepared);
        il.Emit(OpCodes.Brfalse, skip);
        EmitUseWithValue(il, index, bindValue, x => value(x, prepared));
        il.MarkLabel(skip);
    }

    // Emits one nested/path-aware value-array binding slot without closure allocations in emitter bases.
    internal static void EmitUseWithSlot<TEmitter>(ILGenerator il, int index, bool bindValue,
        ParameterMemberAccess member,
        Action<TEmitter, ILGenerator, ParameterMemberAccess> condition,
        Action<TEmitter, ILGenerator, ParameterMemberAccess> value, TEmitter emitter) {
        ClearUseWithSlot(il, index);
        var skip = il.DefineLabel();
        ParameterMemberAccess prepared = member.Prepare(il, skip);
        condition(emitter, il, prepared);
        il.Emit(OpCodes.Brfalse, skip);
        EmitUseWithValue(il, index, bindValue, x => value(emitter, x, prepared));
        il.MarkLabel(skip);
    }

    internal static void ClearUseWithSlot(ILGenerator il, int index) {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
    }

    internal static void EmitDirectValue(ILGenerator il, int index, string key,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue,
        Action<ILGenerator> value) {
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Call, SpanItem);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I1);
        if (!bindValue)
            return;
        if (handlerValue) {
            il.Emit(OpCodes.Ldloc, handlerValues!);
            il.Emit(OpCodes.Ldc_I4, handlerIndex);
            value(il);
            il.Emit(OpCodes.Stelem_Ref);
            return;
        }
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldarg_1);
        value(il);
        il.Emit(OpCodes.Callvirt, DbParamUse);
        il.Emit(OpCodes.Pop);
    }

    internal static void EmitUseWithValue(ILGenerator il, int index, bool bindValue, Action<ILGenerator> value) {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        if (bindValue) value(il);
        else il.Emit(OpCodes.Ldsfld, UsageMarker);
        il.Emit(OpCodes.Stelem_Ref);
    }

    /// <summary>
    /// Helper to load the instance and access the specific direct member.
    /// Handles the difference between value types and class references.
    /// </summary>
    public static void EmitMemberLoad(ILGenerator il, Type targetType, MemberInfo member) {
        if (member is FieldInfo field) {
            if (field.IsStatic)
                il.Emit(OpCodes.Ldsfld, field);
            else {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
            }
            return;
        }

        var meth = member as MethodInfo;
        if (meth is null && member is PropertyInfo prop)
            meth = prop.GetMethod;
        if (meth is null)
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember,
                "The member must be a readable field, property or method.");
        if (meth.IsStatic)
            il.Emit(OpCodes.Call, meth);
        else {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(targetType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, meth);
        }
    }

    internal static void EmitDefaultStackUsage(ILGenerator il, Type targetType, MemberInfo member) {
        Type memberType = ParameterMemberAccess.GetMemberType(member);
        if (!memberType.IsValueType) {
            EmitMemberLoad(il, targetType, member);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }

        Type? nullableType = Nullable.GetUnderlyingType(memberType);
        if (nullableType is null) {
            il.Emit(OpCodes.Ldc_I4_1);
            return;
        }

        EmitMemberLoad(il, targetType, member);
        LocalBuilder value = il.DeclareLocal(memberType);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Call, memberType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
    }

    /// <summary>Boxes the value currently on the IL stack when <paramref name="valueType"/> is a value type.</summary>
    public static void BoxIfNeeded(ILGenerator il, Type valueType) {
        if (valueType.IsValueType)
            il.Emit(OpCodes.Box, valueType);
    }

    /// <summary>Loads a direct member value and boxes it when the member type is a value type.</summary>
    public static void EmitMemberValue(ILGenerator il, Type targetType, MemberInfo member) {
        EmitMemberLoad(il, targetType, member);
        BoxIfNeeded(il, ParameterMemberAccess.GetMemberType(member));
    }
}
