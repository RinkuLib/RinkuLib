using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>Binds one parameter object directly to a database command.</summary>
public delegate object?[] DirectAccessorDelegate(object item, IDbCommand command, DbParamInfo[] parameterInfos, ref Span<bool> usage);

/// <summary>Binds one value-type parameter object directly to a database command without boxing it.</summary>
public delegate object?[] DirectAccessorDelegate<T>(ref T item, IDbCommand command, DbParamInfo[] parameterInfos, ref Span<bool> usage);

/// <summary>Copies one parameter object into a builder's value array.</summary>
public delegate void UseWithAccessorDelegate(object item, object?[] values);

/// <summary>Copies one value-type parameter object into a builder's value array without boxing it.</summary>
public delegate void UseWithAccessorDelegate<T>(ref T item, object?[] values);

internal static class AccessorUsageMarker {
    internal static readonly object Value = new();
}

/// <summary>The direct command-binding delegate for one source type and one query mapper.</summary>
public class DirectAccessor {
    /// <summary>Runs the accessor for a reference type or boxed value type.</summary>
    public readonly DirectAccessorDelegate Invoke;

    internal DirectAccessor(DynamicMethod method)
        => Invoke = method.CreateDelegate<DirectAccessorDelegate>();

    /// <summary>Initializes the object-entry accessor used by a derived value-type accessor.</summary>
    protected DirectAccessor(DirectAccessorDelegate invoke) => Invoke = invoke;
}

/// <summary>The direct command-binding delegate for a value type.</summary>
public sealed class DirectAccessor<T> : DirectAccessor {
    /// <summary>Runs the accessor without boxing <typeparamref name="T"/>.</summary>
    public readonly DirectAccessorDelegate<T> InvokeTyped;

    internal DirectAccessor(DynamicMethod method) : base(CreateBoxedWrapper(method)) {
        InvokeTyped = method.CreateDelegate<DirectAccessorDelegate<T>>();
    }

    private static DirectAccessorDelegate CreateBoxedWrapper(DynamicMethod method) {
        var wrapper = new DynamicMethod($"Boxed_{method.Name}", typeof(object[]),
            [typeof(object), typeof(IDbCommand), typeof(DbParamInfo[]), typeof(Span<bool>).MakeByRefType()],
            typeof(T).Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return wrapper.CreateDelegate<DirectAccessorDelegate>();
    }
}

/// <summary>The value-array delegate used by <c>UseWith</c> for one source type and query mapper.</summary>
public class UseWithAccessor {
    /// <summary>Runs the accessor for a reference type or boxed value type.</summary>
    public readonly UseWithAccessorDelegate Invoke;

    internal UseWithAccessor(DynamicMethod method)
        => Invoke = method.CreateDelegate<UseWithAccessorDelegate>();

    /// <summary>Initializes the object-entry accessor used by a derived value-type accessor.</summary>
    protected UseWithAccessor(UseWithAccessorDelegate invoke) => Invoke = invoke;
}

/// <summary>The value-array delegate used by <c>UseWith</c> for a value type.</summary>
public sealed class UseWithAccessor<T> : UseWithAccessor {
    /// <summary>Runs the accessor without boxing <typeparamref name="T"/>.</summary>
    public readonly UseWithAccessorDelegate<T> InvokeTyped;

    internal UseWithAccessor(DynamicMethod method) : base(CreateBoxedWrapper(method)) {
        InvokeTyped = method.CreateDelegate<UseWithAccessorDelegate<T>>();
    }

    private static UseWithAccessorDelegate CreateBoxedWrapper(DynamicMethod method) {
        var wrapper = new DynamicMethod($"Boxed_{method.Name}", typeof(void),
            [typeof(object), typeof(object[])], typeof(T).Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return wrapper.CreateDelegate<UseWithAccessorDelegate>();
    }
}

/// <summary>
/// Creates the two intentionally separate accessors for the relationship between one parameter-object type and
/// one query mapper. The generated methods contain no lookup, reflection, or attribute inspection.
/// </summary>
public static class ParameterAccessorGenerator {
    /// <summary>Creates a direct accessor for a mapper without special handlers.</summary>
    public static DirectAccessor CreateDirect(Type type, Mapper mapper)
        => CreateDirect(type, mapper, [], mapper.Count, mapper.Count);

    /// <summary>Creates a direct command-binding accessor.</summary>
    public static DirectAccessor CreateDirect(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart)
        => CreateDirectAccessor(type, EmitDirect(type, mapper, handlers, specialHandlerStart, boolConditionStart));

    /// <summary>Creates a builder value-array accessor.</summary>
    public static UseWithAccessor CreateUseWith(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart)
        => CreateUseWithAccessor(type, EmitUseWith(type, mapper, handlers, specialHandlerStart, boolConditionStart));

    /// <summary>Creates a <c>UseWith</c> accessor for a mapper without special handlers.</summary>
    public static UseWithAccessor CreateUseWith(Type type, Mapper mapper)
        => CreateUseWith(type, mapper, [], mapper.Count, mapper.Count);

    private static DirectAccessor CreateDirectAccessor(Type type, DynamicMethod method)
        => type.IsValueType
            ? (DirectAccessor)Activator.CreateInstance(typeof(DirectAccessor<>).MakeGenericType(type),
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null, args: [method], culture: null)!
            : new DirectAccessor(method);

    private static UseWithAccessor CreateUseWithAccessor(Type type, DynamicMethod method)
        => type.IsValueType
            ? (UseWithAccessor)Activator.CreateInstance(typeof(UseWithAccessor<>).MakeGenericType(type),
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null, args: [method], culture: null)!
            : new UseWithAccessor(method);

    private static DynamicMethod EmitDirect(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart) {
        var method = new DynamicMethod($"{type.Name}_DirectParameters", typeof(object[]),
            [type.IsValueType ? type.MakeByRefType() : typeof(object), typeof(IDbCommand), typeof(DbParamInfo[]),
                typeof(Span<bool>).MakeByRefType()], type.Module, skipVisibility: true);
        var il = method.GetILGenerator();

        // A result is complete, never a patch over a prior call's state.
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, typeof(Span<bool>).GetMethod(nameof(Span<bool>.Clear), Type.EmptyTypes)!);

        int handlerValueCount = boolConditionStart - specialHandlerStart;
        LocalBuilder? handlerValues = null;
        if (handlerValueCount > 0) {
            il.Emit(OpCodes.Ldc_I4, handlerValueCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            handlerValues = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, handlerValues);
        }

        var plan = CreatePlan(type, mapper);
        for (int index = 0; index < mapper.Count; index++)
            EmitSlot(il, AccessPath.Direct, plan, index, type, mapper, handlers, specialHandlerStart,
                boolConditionStart, handlerValues);

        if (handlerValues is null)
            il.Emit(OpCodes.Call, typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(object)));
        else
            il.Emit(OpCodes.Ldloc, handlerValues);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static DynamicMethod EmitUseWith(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart) {
        var method = new DynamicMethod($"{type.Name}_UseWith", typeof(void),
            [type.IsValueType ? type.MakeByRefType() : typeof(object), typeof(object[])], type.Module, skipVisibility: true);
        var il = method.GetILGenerator();
        var plan = CreatePlan(type, mapper);
        for (int index = 0; index < mapper.Count; index++)
            EmitSlot(il, AccessPath.UseWith, plan, index, type, mapper, handlers, specialHandlerStart,
                boolConditionStart, handlerValues: null);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static AccessPlan CreatePlan(Type type, Mapper mapper) {
        var members = new MemberInfo?[mapper.Count];
        char variablePrefix = mapper.Count == 0 || mapper.Keys[0].Length == 0 ? default : mapper.Keys[0][0];
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) {
            if (member is not FieldInfo and not PropertyInfo)
                continue;

            if (variablePrefix != default) {
                int parameterIndex = mapper.GetIndex(variablePrefix, member.Name);
                if (parameterIndex >= 0)
                    members[parameterIndex] ??= member;
            }
            int conditionIndex = mapper.GetIndex(member.Name);
            if (conditionIndex >= 0)
                members[conditionIndex] ??= member;
        }
        return new AccessPlan(members, type.GetCustomAttributes<AccessorEmitterHandler>().ToArray(), variablePrefix);
    }

    /// <summary>
    /// Resolves one mapper slot once. The branch is taken while generating, never by the emitted delegate:
    /// direct and builder delegates therefore remain specialized without duplicating rule resolution.
    /// </summary>
    private static void EmitSlot(ILGenerator il, AccessPath path, AccessPlan plan, int index, Type type, Mapper mapper,
        SpecialHandler[] handlers, int specialHandlerStart, int boolConditionStart, LocalBuilder? handlerValues) {
        var member = plan.Members[index];
        if (member is not null) {
            var emitter = member.GetCustomAttribute<AccessorEmitterHandler>()?.GetMemberEmitter(plan.VariablePrefix, index, type, member, mapper);
            if (emitter is not null) {
                emitter.Validate(type, member);
                EmitCustom(path, emitter, il, index, mapper.Keys[index], type, member, handlerValues,
                    specialHandlerStart, boolConditionStart);
                return;
            }

            // A type attribute supplies the member's default rule. A member attribute always wins.
            foreach (var handler in plan.TypeHandlers)
                if (handler.GetMemberEmitter(plan.VariablePrefix, index, type, member, mapper) is { } typeEmitter) {
                    typeEmitter.Validate(type, member);
                    EmitCustom(path, typeEmitter, il, index, mapper.Keys[index], type, member, handlerValues,
                        specialHandlerStart, boolConditionStart);
                    return;
                }

            EmitDefault(path, il, index, mapper.Keys[index], type, member, handlers, specialHandlerStart,
                boolConditionStart, handlerValues);
            return;
        }

        foreach (var handler in plan.TypeHandlers)
            if (handler.GetTypeEmitter(plan.VariablePrefix, index, type, mapper) is { } emitter) {
                emitter.Validate(type);
                EmitTypeCustom(path, emitter, il, index, mapper.Keys[index], type, handlerValues,
                    specialHandlerStart, boolConditionStart);
                return;
            }

        // UseWith replaces every slot. A key with no source member is deliberately off.
        if (path == AccessPath.UseWith) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static void EmitCustom(AccessPath path, IAccessorEmitter emitter, ILGenerator il, int index, string key,
        Type type, MemberInfo member, LocalBuilder? handlerValues, int specialHandlerStart, int boolConditionStart) {
        if (path == AccessPath.Direct)
            emitter.Emit(il, index, key, type, member, handlerValues, index - specialHandlerStart,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart);
        else
            emitter.EmitUseWith(il, index, type, member, index < boolConditionStart);
    }

    private static void EmitTypeCustom(AccessPath path, ITypeAccessorEmitter emitter, ILGenerator il, int index,
        string key, Type type, LocalBuilder? handlerValues, int specialHandlerStart, int boolConditionStart) {
        if (path == AccessPath.Direct)
            emitter.Emit(il, index, key, type, handlerValues, index - specialHandlerStart,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart);
        else
            emitter.EmitUseWith(il, index, type, index < boolConditionStart);
    }

    private static void EmitDefault(AccessPath path, ILGenerator il, int index, string key, Type type, MemberInfo member,
        SpecialHandler[] handlers, int specialHandlerStart, int boolConditionStart, LocalBuilder? handlerValues) {
        int handlerIndex = index - specialHandlerStart;
        Action<ILGenerator> condition = index >= specialHandlerStart && handlerIndex >= 0 && handlerIndex < handlers.Length
            ? handlers[handlerIndex].GetUsageEmitter(type, member) ?? (x => EmitDefaultUsage(x, type, member))
            : x => EmitDefaultUsage(x, type, member);
        if (path == AccessPath.Direct)
            AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart,
                condition, x => AccessorEmitter.EmitMemberValue(x, type, member));
        else
            AccessorEmitter.EmitUseWithSlot(il, index, index < boolConditionStart, condition,
                x => AccessorEmitter.EmitMemberValue(x, type, member));
    }

    private static void EmitDefaultUsage(ILGenerator il, Type type, MemberInfo member) {
        var memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (!memberType.IsValueType) {
            AccessorEmitter.EmitMemberLoad(il, type, member);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }
        if (Nullable.GetUnderlyingType(memberType) is { }) {
            AccessorEmitter.EmitMemberLoad(il, type, member);
            var value = il.DeclareLocal(memberType);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, memberType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            return;
        }
        il.Emit(OpCodes.Ldc_I4_1);
    }

    private sealed record AccessPlan(MemberInfo?[] Members, AccessorEmitterHandler[] TypeHandlers, char VariablePrefix);

    private enum AccessPath { Direct, UseWith }
}
