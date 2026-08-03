using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>Runs the generated parameter-object binding plan for a reference-type object.</summary>
public delegate object?[] ParameterBinderDelegate(object item, IDbCommand command, DbParamInfo[] parameterInfos, ref Span<bool> usage);

/// <summary>Runs the generated parameter-object binding plan for a value-type object by reference.</summary>
public delegate object?[] ParameterBinderDelegate<T>(ref T item, IDbCommand command, DbParamInfo[] parameterInfos, ref Span<bool> usage);

/// <summary>Populates a value array from a reference-type parameter object.</summary>
public delegate void ParameterObjectBinderDelegate(object item, object?[] values);

/// <summary>Populates a value array from a value-type parameter object by reference.</summary>
public delegate void ParameterObjectBinderDelegate<T>(ref T item, object?[] values);

internal static class AccessorUsageMarker {
    internal static readonly object Value = new();
}

/// <summary>
/// The generated binding plan for a parameter-object type. The generated method is a linear sequence: every
/// mapped member emits its configured usability check, then either skips the slot or binds it. The returned
/// array contains handler values only.
/// </summary>
public class TypeAccessorCache {
    /// <summary>Executes the generated binding plan.</summary>
    public ParameterBinderDelegate Bind;

    /// <summary>Initializes an empty cache for a derived cache implementation.</summary>
    protected TypeAccessorCache() => Bind = default!;

    /// <summary>Creates a cache from a generated binding method.</summary>
    public TypeAccessorCache(DynamicMethod method)
        => Bind = method.CreateDelegate<ParameterBinderDelegate>(null);
}

/// <summary>The generated binding plan for a value type, with a by-reference entry point.</summary>
public sealed class StructTypeAccessorCache<T> : TypeAccessorCache {
    /// <summary>Executes the generated binding plan without boxing the value type.</summary>
    public ParameterBinderDelegate<T> GenericBind;

    /// <summary>Creates a by-reference cache from a generated binding method.</summary>
    public StructTypeAccessorCache(DynamicMethod method) : base() {
        GenericBind = method.CreateDelegate<ParameterBinderDelegate<T>>(null);
        Bind = CreateBoxedWrapper(method);
    }

    private static ParameterBinderDelegate CreateBoxedWrapper(DynamicMethod method) {
        var wrapper = new DynamicMethod($"BoxedWrapper_{method.Name}", typeof(object[]),
            [typeof(object), typeof(object), typeof(IDbCommand), typeof(DbParamInfo[]), typeof(Span<bool>).MakeByRefType()],
            typeof(T).Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return wrapper.CreateDelegate<ParameterBinderDelegate>(null);
    }
}

/// <summary>The generated <c>UseWith</c> binding plan for a reference-type object.</summary>
public class UseWithAccessorCache {
    /// <summary>Executes the generated value-array binding plan.</summary>
    public ParameterObjectBinderDelegate Bind;

    /// <summary>Initializes an empty cache for a derived cache implementation.</summary>
    protected UseWithAccessorCache() => Bind = default!;

    /// <summary>Creates a cache from a generated <c>UseWith</c> method.</summary>
    public UseWithAccessorCache(DynamicMethod method)
        => Bind = method.CreateDelegate<ParameterObjectBinderDelegate>(null);
}

/// <summary>The generated <c>UseWith</c> binding plan for a value type.</summary>
public sealed class StructUseWithAccessorCache<T> : UseWithAccessorCache {
    /// <summary>Executes the generated value-array binding plan without boxing.</summary>
    public ParameterObjectBinderDelegate<T> GenericBind;

    /// <summary>Creates a value-type cache from a generated <c>UseWith</c> method.</summary>
    public StructUseWithAccessorCache(DynamicMethod method) : base() {
        GenericBind = method.CreateDelegate<ParameterObjectBinderDelegate<T>>(null);
        Bind = CreateBoxedWrapper(method);
    }

    private static ParameterObjectBinderDelegate CreateBoxedWrapper(DynamicMethod method) {
        var wrapper = new DynamicMethod($"BoxedUseWithWrapper_{method.Name}", typeof(void),
            [typeof(object), typeof(object), typeof(object[])], typeof(T).Module, skipVisibility: true);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        return wrapper.CreateDelegate<ParameterObjectBinderDelegate>(null);
    }
}

/// <summary>
/// Builds and caches the generated parameter-object binding plan for a type and mapper.
/// </summary>
public static class TypeAccessorCacher<T> {
    /// <summary>Guards generation of a new binding plan.</summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        SharedLock = new();

    private static (object Mapper, SpecialHandler[]? SpecialHandlers, int HandlersStart, int BoolCondStart,
        object? AccessorHandlers, TypeAccessorCache Cache)[] DirectVariants = [];

    private static (object Mapper, SpecialHandler[]? SpecialHandlers, int HandlersStart, int BoolCondStart,
        object? AccessorHandlers, UseWithAccessorCache Cache)[] UseWithVariants = [];

    /// <summary>Gets or creates the cached binding plan without runtime accessor registrations.</summary>
    public static TypeAccessorCache GetOrGenerate(Mapper mapper, SpecialHandler[]? handlers = null,
        int handlersStart = 0, int boolCondStart = -1) {
        boolCondStart = boolCondStart < 0 ? handlersStart : boolCondStart;
        var currentVariants = DirectVariants;
        foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                && Start == handlersStart && BoolStart == boolCondStart && AccessorHandlers is null)
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in DirectVariants)
                if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                    && Start == handlersStart && BoolStart == boolCondStart && AccessorHandlers is null)
                    return Cache;
            TypeAccessorCache cache = CreateCache(GenerateDelegate(mapper, handlers, handlersStart, boolCondStart, null));
            DirectVariants = [.. DirectVariants, (mapper, handlers, handlersStart, boolCondStart, null, cache)];
            return cache;
        }
    }

    /// <summary>Gets or creates the cached binding plan with runtime accessor registrations.</summary>
    public static TypeAccessorCache GetOrGenerate(Mapper mapper,
        IReadOnlyList<AccessorHandlerRegistration> registrations,
        SpecialHandler[]? handlers = null, int handlersStart = 0, int boolCondStart = -1) {
        ArgumentNullException.ThrowIfNull(registrations);
        boolCondStart = boolCondStart < 0 ? handlersStart : boolCondStart;
        var currentVariants = DirectVariants;
        foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                && Start == handlersStart && BoolStart == boolCondStart
                && ReferenceEquals(AccessorHandlers, registrations))
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in DirectVariants)
                if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                    && Start == handlersStart && BoolStart == boolCondStart
                    && ReferenceEquals(AccessorHandlers, registrations))
                    return Cache;
            TypeAccessorCache cache = CreateCache(GenerateDelegate(mapper, handlers, handlersStart, boolCondStart, registrations));
            DirectVariants = [.. DirectVariants, (mapper, handlers, handlersStart, boolCondStart, registrations, cache)];
            return cache;
        }
    }

    /// <summary>Gets or creates the independent <c>UseWith</c> binding plan without runtime registrations.</summary>
    public static UseWithAccessorCache GetOrGenerateUseWith(Mapper mapper, SpecialHandler[]? handlers = null,
        int handlersStart = 0, int boolCondStart = -1) {
        boolCondStart = boolCondStart < 0 ? handlersStart : boolCondStart;
        var currentVariants = UseWithVariants;
        foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                && Start == handlersStart && BoolStart == boolCondStart && AccessorHandlers is null)
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in UseWithVariants)
                if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                    && Start == handlersStart && BoolStart == boolCondStart && AccessorHandlers is null)
                    return Cache;
            var cache = CreateUseWithCache(GenerateUseWithDelegate(mapper, handlers, handlersStart, boolCondStart, null));
            UseWithVariants = [.. UseWithVariants, (mapper, handlers, handlersStart, boolCondStart, null, cache)];
            return cache;
        }
    }

    /// <summary>Gets or creates the independent <c>UseWith</c> binding plan with runtime registrations.</summary>
    public static UseWithAccessorCache GetOrGenerateUseWith(Mapper mapper,
        IReadOnlyList<AccessorHandlerRegistration> registrations,
        SpecialHandler[]? handlers = null, int handlersStart = 0, int boolCondStart = -1) {
        ArgumentNullException.ThrowIfNull(registrations);
        boolCondStart = boolCondStart < 0 ? handlersStart : boolCondStart;
        var currentVariants = UseWithVariants;
        foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                && Start == handlersStart && BoolStart == boolCondStart
                && ReferenceEquals(AccessorHandlers, registrations))
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, SpecialHandlers, Start, BoolStart, AccessorHandlers, Cache) in UseWithVariants)
                if (ReferenceEquals(Keys, mapper) && ReferenceEquals(SpecialHandlers, handlers)
                    && Start == handlersStart && BoolStart == boolCondStart
                    && ReferenceEquals(AccessorHandlers, registrations))
                    return Cache;
            var cache = CreateUseWithCache(GenerateUseWithDelegate(mapper, handlers, handlersStart, boolCondStart, registrations));
            UseWithVariants = [.. UseWithVariants, (mapper, handlers, handlersStart, boolCondStart, registrations, cache)];
            return cache;
        }
    }

    private static TypeAccessorCache CreateCache(DynamicMethod method)
        => typeof(T).IsValueType ? new StructTypeAccessorCache<T>(method) : new TypeAccessorCache(method);

    private static UseWithAccessorCache CreateUseWithCache(DynamicMethod method)
        => typeof(T).IsValueType ? new StructUseWithAccessorCache<T>(method) : new UseWithAccessorCache(method);

    private static DynamicMethod GenerateDelegate(Mapper mapper, SpecialHandler[]? handlers,
        int handlersStart, int boolCondStart, IReadOnlyList<AccessorHandlerRegistration>? registrations) {
        var varChar = mapper.Count <= 0 ? default : mapper.Keys[0].Length <= 0 ? default : mapper.Keys[0][0];
        Type type = typeof(T);
        Type arg0 = type.IsValueType ? type.MakeByRefType() : typeof(object);
        var method = new DynamicMethod($"{type.Name}_Bind", typeof(object[]),
            [typeof(object), arg0, typeof(IDbCommand), typeof(DbParamInfo[]), typeof(Span<bool>).MakeByRefType()],
            type.Module, skipVisibility: true);
        var il = method.GetILGenerator();

        int handlerValuesCount = Math.Max(0, boolCondStart - handlersStart);
        LocalBuilder? handlerValues = null;
        if (handlerValuesCount > 0) {
            il.Emit(OpCodes.Ldc_I4, handlerValuesCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            handlerValues = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, handlerValues);
        }

        int count = mapper.Count;
        var (usagePlans, valuePlans) = MakePlans(type, mapper, handlers, handlersStart, registrations, varChar);

        for (int i = 0; i < count; i++) {
            var usage = usagePlans[i];
            if (usage is null)
                continue;
            var skip = il.DefineLabel();
            usage.Emit(il);
            il.Emit(OpCodes.Brfalse, skip);
            EmitUsageSet(il, i);

            if (i < handlersStart) {
                EmitNormalBinding(il, i, mapper.Keys[i], valuePlans[i]);
            }
            else if (i < boolCondStart) {
                EmitHandlerValue(il, handlerValues!, i - handlersStart, valuePlans[i]);
            }
            il.MarkLabel(skip);
        }

        if (handlerValues is null)
            il.Emit(OpCodes.Call, typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(object)));
        else
            il.Emit(OpCodes.Ldloc, handlerValues);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static DynamicMethod GenerateUseWithDelegate(Mapper mapper, SpecialHandler[]? handlers,
        int handlersStart, int boolCondStart, IReadOnlyList<AccessorHandlerRegistration>? registrations) {
        var varChar = mapper.Count <= 0 ? default : mapper.Keys[0].Length <= 0 ? default : mapper.Keys[0][0];
        Type type = typeof(T);
        Type arg0 = type.IsValueType ? type.MakeByRefType() : typeof(object);
        var method = new DynamicMethod($"{type.Name}_UseWith", typeof(void),
            [typeof(object), arg0, typeof(object[])], type.Module, skipVisibility: true);
        var il = method.GetILGenerator();
        int count = mapper.Count;
        var (usagePlans, valuePlans) = MakePlans(type, mapper, handlers, handlersStart, registrations, varChar);

        for (int i = 0; i < count; i++) {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stelem_Ref);

            var usage = usagePlans[i];
            if (usage is null)
                continue;

            var skip = il.DefineLabel();
            usage.Emit(il);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4, i);
            if (i < boolCondStart)
                valuePlans[i]?.Emit(il);
            else
                il.Emit(OpCodes.Ldsfld, typeof(AccessorUsageMarker).GetField(nameof(AccessorUsageMarker.Value), BindingFlags.Static | BindingFlags.NonPublic)!);
            il.Emit(OpCodes.Stelem_Ref);
            il.MarkLabel(skip);
        }

        il.Emit(OpCodes.Ret);
        return method;
    }

    private static (IAccessorEmiter?[] Usage, IAccessorEmiter?[] Values) MakePlans(
        Type type, Mapper mapper, SpecialHandler[]? handlers, int handlersStart,
        IReadOnlyList<AccessorHandlerRegistration>? registrations, char varChar) {
        int count = mapper.Count;
        IAccessorEmiter?[] usagePlans = new IAccessorEmiter?[count];
        IAccessorEmiter?[] valuePlans = new IAccessorEmiter?[count];
        var typeHandlers = type.GetCustomAttributes<AccessorEmiterHandler>()
            .Concat(registrations is null ? [] : registrations.Where(x => x.Member is null).Select(x => x.Handler));
        foreach (var handler in typeHandlers)
            handler.HandleEmit(varChar, usagePlans, valuePlans, type, null, mapper);

        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        foreach (var member in members) {
            var runtimeHandler = registrations?.FirstOrDefault(x => ReferenceEquals(x.Member, member)).Handler;
            var handler = runtimeHandler ?? member.GetCustomAttribute<AccessorEmiterHandler>();
            if (handler is not null) {
                handler.HandleEmit(varChar, usagePlans, valuePlans, type, member, mapper);
                continue;
            }

            if (member is FieldInfo or PropertyInfo) {
                int index = mapper.GetIndex(varChar, member.Name);
                if (index >= 0) {
                    var handled = handlers is null ? -1 : index - handlersStart;
                    usagePlans[index] = (handled >= 0 && handled < (handlers?.Length ?? 0)
                            ? handlers![handled].GetUsageEmitter(type, member)
                            : null)
                        ?? new MemberUsageEmitter(type, member);
                    valuePlans[index] = new MemberValueEmitter(type, member);
                }
            }
        }
        return (usagePlans, valuePlans);
    }

    private static void EmitUsageSet(ILGenerator il, int index) {
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Call, typeof(Span<bool>).GetProperty("Item")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stind_I1);
    }

    private static void EmitNormalBinding(ILGenerator il, int index, string key, IAccessorEmiter? value) {
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldarg_2);
        value?.Emit(il);
        il.Emit(OpCodes.Callvirt, typeof(DbParamInfo).GetMethod(nameof(DbParamInfo.Use), [typeof(string), typeof(IDbCommand), typeof(object)])!);
        il.Emit(OpCodes.Pop);
    }

    private static void EmitHandlerValue(ILGenerator il, LocalBuilder handlerValues, int index, IAccessorEmiter? value) {
        il.Emit(OpCodes.Ldloc, handlerValues);
        il.Emit(OpCodes.Ldc_I4, index);
        value?.Emit(il);
        il.Emit(OpCodes.Stelem_Ref);
    }
}
