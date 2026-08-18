using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;
using Rinku.Querying;

namespace Rinku.Querying.Parameters;

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

    internal DirectAccessor(DynamicMethod method) : base(CreateBoxedWrapper(method))
        => InvokeTyped = method.CreateDelegate<DirectAccessorDelegate<T>>();

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

    internal UseWithAccessor(DynamicMethod method) : base(CreateBoxedWrapper(method))
        => InvokeTyped = method.CreateDelegate<UseWithAccessorDelegate<T>>();

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

/// <summary>Creates cached parameter accessors.</summary>
public static class ParameterAccessorGenerator {
    /// <summary>Creates a direct command-binding accessor.</summary>
    public static DirectAccessor CreateDirect(Type type, Mapper mapper)
        => CreateDirect(type, mapper, [], mapper.Count, mapper.Count);

    /// <summary>Creates a direct command-binding accessor with query handlers.</summary>
    public static DirectAccessor CreateDirect(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart)
        => CreateDirectAccessor(type, EmitDirect(type, mapper, handlers, specialHandlerStart, boolConditionStart));

    /// <summary>Creates a builder value accessor with query handlers.</summary>
    public static UseWithAccessor CreateUseWith(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart)
        => CreateUseWithAccessor(type, EmitUseWith(type, mapper, handlers, specialHandlerStart, boolConditionStart));

    /// <summary>Creates a builder value accessor.</summary>
    public static UseWithAccessor CreateUseWith(Type type, Mapper mapper)
        => CreateUseWith(type, mapper, [], mapper.Count, mapper.Count);

    /// <summary>Creates an IL-emission accessor that can leave resolved values typed on the evaluation stack.</summary>
    public static StackAccessor CreateStack(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart)
        => new(type, mapper, handlers, specialHandlerStart, boolConditionStart, CreatePlan(type, mapper));

    /// <summary>Creates a stack accessor without query-special handlers.</summary>
    public static StackAccessor CreateStack(Type type, Mapper mapper)
        => CreateStack(type, mapper, [], mapper.Count, mapper.Count);

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

    /// <summary>
    /// Reuses the normal parameter-shape plan but emits raw typed values instead of binding them.
    /// The source object is expected in argument 0 of the dynamic method being generated.
    /// </summary>
    /// <summary>Describes typed parameter slots for IL generation.</summary>
    public sealed class StackAccessor {
        internal readonly Type Type;
        internal readonly Mapper Mapper;
        internal readonly SpecialHandler[] Handlers;
        internal readonly int SpecialHandlerStart;
        internal readonly int BoolConditionStart;
        internal readonly AccessPlan Plan;

        internal StackAccessor(Type type, Mapper mapper, SpecialHandler[] handlers,
            int specialHandlerStart, int boolConditionStart, AccessPlan plan) {
            Type = type;
            Mapper = mapper;
            Handlers = handlers;
            SpecialHandlerStart = specialHandlerStart;
            BoolConditionStart = boolConditionStart;
            Plan = plan;
        }

        // Starts one emission session for one IL generator.
        internal StackAccessorEmission Begin(ILGenerator il) => new(this, il);

        internal bool RequiresPreparation(int index)
            => Plan.Sources[index] is DictionarySource || Plan.Sources[index] is MemberSource { Access.IsNested: true };

        // Returns the raw slot type without starting an IL emission session.
        internal Type? GetValueType(int index) => GetStackType(this, index);
    }

    /// <summary>
    /// Per-IL-generator state for stack emission. Usage must be emitted before value for nested and dictionary sources.
    /// </summary>
    /// <summary>Holds one typed parameter emission session.</summary>
    public sealed class StackAccessorEmission {
        private readonly StackAccessor _accessor;
        private readonly ILGenerator _il;
        private readonly ParameterMemberAccess?[] _preparedMembers;
        private readonly LocalBuilder?[] _dictionaryValues;
        private readonly bool[] _usageEmitted;

        internal StackAccessorEmission(StackAccessor accessor, ILGenerator il) {
            _accessor = accessor;
            _il = il;
            int count = accessor.Mapper.Count;
            _preparedMembers = new ParameterMemberAccess?[count];
            _dictionaryValues = new LocalBuilder?[count];
            _usageEmitted = new bool[count];
        }

        // Returns the raw typed value that this slot emits, or null when no source exists.
        internal Type? GetValueType(int index)
            => GetStackType(_accessor, index);

        // Emits a boolean indicating whether the slot is usable and prepares any nested/dictionary locals.
        internal void EmitUsage(int index) {
            EmitStackUsage(_accessor, this, index);
            _usageEmitted[index] = true;
        }

        // Emits the raw typed slot value directly onto the evaluation stack.
        internal void EmitValue(int index) {
            if (!_usageEmitted[index] && _accessor.RequiresPreparation(index))
                throw new InvalidOperationException("EmitUsage must be called before EmitValue for a nested or dictionary parameter source.");
            EmitStackValue(_accessor, this, index);
        }

        internal ILGenerator IL => _il;
        internal ParameterMemberAccess?[] PreparedMembers => _preparedMembers;
        internal LocalBuilder?[] DictionaryValues => _dictionaryValues;
    }

    private static DynamicMethod EmitDirect(Type type, Mapper mapper, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart) {
        var method = new DynamicMethod($"{type.Name}_DirectParameters", typeof(object[]),
            [type.IsValueType ? type.MakeByRefType() : typeof(object), typeof(IDbCommand), typeof(DbParamInfo[]),
                typeof(Span<bool>).MakeByRefType()], type.Module, skipVisibility: true);
        var il = method.GetILGenerator();

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

        AccessPlan plan = CreatePlan(type, mapper);
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
        AccessPlan plan = CreatePlan(type, mapper);
        for (int index = 0; index < mapper.Count; index++)
            EmitSlot(il, AccessPath.UseWith, plan, index, type, mapper, handlers, specialHandlerStart,
                boolConditionStart, handlerValues: null);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static AccessPlan CreatePlan(Type type, Mapper mapper) {
        var sources = new ParameterSource?[mapper.Count];
        char variablePrefix = GetVariablePrefix(mapper);
        ParameterConflictBehavior conflictBehavior = GetConflictBehavior(type);

        if (DictionaryParameterAccess.DictionaryShape.TryCreate(type, out var rootDictionary))
            AddDictionarySources(type, container: null, rootDictionary!, prefix: string.Empty, depth: 0,
                mapper, variablePrefix, sources, conflictBehavior);
        else {
            var activeTypes = new HashSet<Type>();
            DiscoverMembers(type, type, [], string.Empty, mapper, variablePrefix, sources, activeTypes, conflictBehavior);
        }

        var handlers = new List<AccessorEmitterHandler>(type.GetCustomAttributes<AccessorEmitterHandler>(inherit: true));
        foreach (Type contract in type.GetInterfaces())
            foreach (AccessorEmitterHandler handler in contract.GetCustomAttributes<AccessorEmitterHandler>(inherit: true))
                if (!handlers.Any(existing => existing.GetType() == handler.GetType())) handlers.Add(handler);
        return new AccessPlan(sources, handlers.ToArray(), variablePrefix);
    }

    private static void DiscoverMembers(Type rootType, Type currentType, MemberInfo[] parentPath, string prefix,
        Mapper mapper, char variablePrefix, ParameterSource?[] sources, HashSet<Type> activeTypes,
        ParameterConflictBehavior conflictBehavior) {
        Type discoveryType = Nullable.GetUnderlyingType(currentType) ?? currentType;
        if (!activeTypes.Add(discoveryType))
            throw new InvalidOperationException($"Nested parameter configuration contains a cycle through '{discoveryType}'.");

        try {
            MemberInfo[] members = discoveryType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

            // Ordinary members first. A member declared directly on the wrapper beats a flattened child.
            for (int i = 0; i < members.Length; i++) {
                MemberInfo member = members[i];
                if (!IsReadableParameterMember(member) || member.IsDefined(typeof(ParameterIgnoreAttribute), inherit: true)
                    || member.IsDefined(typeof(NestedParametersAttribute), inherit: true))
                    continue;

                MemberInfo[] path = Append(parentPath, member);
                var access = new ParameterMemberAccess(rootType, path);
                ParameterNameAttribute? rename = member.GetCustomAttribute<ParameterNameAttribute>(inherit: true);
                if (rename is null)
                    AddNamedSource(member.Name, prefix, new MemberSource(access, explicitName: false), mapper, variablePrefix, sources, conflictBehavior);
                else
                    AddNamedSource(rename.Name, prefix, new MemberSource(access, explicitName: true), mapper, variablePrefix, sources, conflictBehavior);
                foreach (ParameterAliasAttribute alias in member.GetCustomAttributes<ParameterAliasAttribute>(inherit: true))
                    AddNamedSource(alias.Name, prefix, new MemberSource(access, explicitName: true), mapper, variablePrefix, sources, conflictBehavior);
            }

            // Explicitly flattened members second.
            for (int i = 0; i < members.Length; i++) {
                MemberInfo member = members[i];
                if (!IsReadableParameterMember(member) || member.IsDefined(typeof(ParameterIgnoreAttribute), inherit: true))
                    continue;
                NestedParametersAttribute? nested = member.GetCustomAttribute<NestedParametersAttribute>(inherit: true);
                if (nested is null)
                    continue;
                if (IsStatic(member))
                    throw new InvalidOperationException($"Nested parameter member '{member.Name}' cannot be static.");

                MemberInfo[] path = Append(parentPath, member);
                Type memberType = ParameterMemberAccess.GetMemberType(member);
                Type nestedType = Nullable.GetUnderlyingType(memberType) ?? memberType;
                string nestedPrefix = prefix + (nested.Prefix ?? string.Empty);
                var container = new ParameterMemberAccess(rootType, path);

                if (DictionaryParameterAccess.DictionaryShape.TryCreate(nestedType, out var dictionary)) {
                    AddDictionarySources(rootType, container, dictionary!, nestedPrefix, path.Length,
                        mapper, variablePrefix, sources, conflictBehavior);
                    continue;
                }

                DiscoverMembers(rootType, nestedType, path, nestedPrefix, mapper, variablePrefix, sources, activeTypes, conflictBehavior);
            }
        }
        finally {
            activeTypes.Remove(discoveryType);
        }
    }

    private static void AddNamedSource(string name, string prefix, MemberSource source, Mapper mapper,
        char variablePrefix, ParameterSource?[] sources, ParameterConflictBehavior conflictBehavior) {
        string logicalName = prefix + NormalizeName(name, variablePrefix);
        if (logicalName.Length == 0)
            return;
        if (variablePrefix != default)
            AddSource(sources, mapper.GetIndex(variablePrefix, logicalName), source, variablePrefix + logicalName, conflictBehavior);
        AddSource(sources, mapper.GetIndex(logicalName), source, logicalName, conflictBehavior);
    }

    private static void AddDictionarySources(Type rootType, ParameterMemberAccess? container,
        DictionaryParameterAccess.DictionaryShape shape, string prefix, int depth, Mapper mapper,
        char variablePrefix, ParameterSource?[] sources, ParameterConflictBehavior conflictBehavior) {
        for (int index = 0; index < mapper.Count; index++) {
            string logicalName = GetLogicalName(mapper.Keys[index], variablePrefix);
            if (prefix.Length != 0) {
                if (!logicalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                logicalName = logicalName[prefix.Length..];
            }
            if (logicalName.Length == 0)
                continue;
            string? fallbackKey = container is null && !string.Equals(mapper.Keys[index], logicalName, StringComparison.Ordinal)
                ? mapper.Keys[index]
                : null;
            var access = new DictionaryParameterAccess(rootType, container, shape, logicalName, fallbackKey, depth);
            AddSource(sources, index, new DictionarySource(access), mapper.Keys[index], conflictBehavior);
        }
    }

    private static void AddSource(ParameterSource?[] sources, int index, ParameterSource candidate, string key,
        ParameterConflictBehavior conflictBehavior) {
        if (index < 0)
            return;
        ParameterSource? existing = sources[index];
        if (existing is null) {
            sources[index] = candidate;
            return;
        }
        if (existing.SameSource(candidate))
            return;
        if (candidate.Depth < existing.Depth) {
            sources[index] = candidate;
            return;
        }
        if (candidate.Depth > existing.Depth)
            return;
        if (candidate.KindPriority < existing.KindPriority) {
            sources[index] = candidate;
            return;
        }
        if (candidate.KindPriority > existing.KindPriority)
            return;
        if (conflictBehavior == ParameterConflictBehavior.TakeOne)
            return;
        throw new InvalidOperationException(
            $"Parameter '{key}' is provided by both {existing.Description} and {candidate.Description} at the same nesting depth and priority.");
    }

    private static void EmitSlot(ILGenerator il, AccessPath path, AccessPlan plan, int index, Type type, Mapper mapper,
        SpecialHandler[] handlers, int specialHandlerStart, int boolConditionStart, LocalBuilder? handlerValues) {
        ParameterSource? source = plan.Sources[index];
        if (source is MemberSource memberSource) {
            EmitMemberSlot(il, path, plan, index, type, mapper, memberSource.Access,
                handlers, specialHandlerStart, boolConditionStart, handlerValues);
            return;
        }
        if (source is DictionarySource dictionarySource) {
            EmitDictionarySlot(il, path, index, mapper.Keys[index], dictionarySource.Access,
                specialHandlerStart, boolConditionStart, handlerValues);
            return;
        }

        if (GetTypeEmitter(plan, index, type, mapper) is { } emitter) {
            emitter.Validate(type);
            EmitTypeCustom(path, emitter, il, index, mapper.Keys[index], type, handlerValues,
                specialHandlerStart, boolConditionStart);
            return;
        }

        if (path == AccessPath.UseWith)
            AccessorEmitter.ClearUseWithSlot(il, index);
    }

    private static IAccessorEmitter? GetMemberEmitter(AccessPlan plan, int index, Type type, Mapper mapper,
        ParameterMemberAccess member) {
        IAccessorEmitter? emitter = member.Member.GetCustomAttribute<AccessorEmitterHandler>(inherit: true)
            ?.GetMemberEmitter(plan.VariablePrefix, index, type, member.Member, mapper);
        if (emitter is not null)
            return emitter;
        foreach (AccessorEmitterHandler handler in plan.TypeHandlers)
            if (handler.GetMemberEmitter(plan.VariablePrefix, index, type, member.Member, mapper) is { } typeEmitter)
                return typeEmitter;
        return null;
    }

    private static ITypeAccessorEmitter? GetTypeEmitter(AccessPlan plan, int index, Type type, Mapper mapper) {
        foreach (AccessorEmitterHandler handler in plan.TypeHandlers)
            if (handler.GetTypeEmitter(plan.VariablePrefix, index, type, mapper) is { } emitter)
                return emitter;
        return null;
    }

    private static void EmitMemberSlot(ILGenerator il, AccessPath path, AccessPlan plan, int index, Type type,
        Mapper mapper, ParameterMemberAccess member, SpecialHandler[] handlers,
        int specialHandlerStart, int boolConditionStart, LocalBuilder? handlerValues) {
        IAccessorEmitter? emitter = GetMemberEmitter(plan, index, type, mapper, member);

        if (emitter is not null) {
            EmitCustom(path, emitter, il, index, mapper.Keys[index], member, handlerValues,
                specialHandlerStart, boolConditionStart);
            return;
        }

        EmitDefault(path, il, index, mapper.Keys[index], member, handlers,
            specialHandlerStart, boolConditionStart, handlerValues);
    }

    private static void EmitCustom(AccessPath path, IAccessorEmitter emitter, ILGenerator il, int index, string key,
        ParameterMemberAccess member, LocalBuilder? handlerValues, int specialHandlerStart, int boolConditionStart) {
        if (member.IsNested) {
            if (emitter is not IPathAccessorEmitter pathEmitter)
                throw new InvalidOperationException(
                    $"Parameter rule '{emitter.GetType()}' on nested member '{member.Member.Name}' does not support nested parameter paths. " +
                    $"Implement {nameof(IPathAccessorEmitter)} or derive from {nameof(PathAccessorEmitterBase)}.");
            pathEmitter.Validate(member);
            if (path == AccessPath.Direct)
                pathEmitter.Emit(il, index, key, member, handlerValues, index - specialHandlerStart,
                    index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart);
            else
                pathEmitter.EmitUseWith(il, index, member, index < boolConditionStart);
            return;
        }

        emitter.Validate(member.RootType, member.Member);
        if (path == AccessPath.Direct)
            emitter.Emit(il, index, key, member.RootType, member.Member, handlerValues, index - specialHandlerStart,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart);
        else
            emitter.EmitUseWith(il, index, member.RootType, member.Member, index < boolConditionStart);
    }

    private static void EmitTypeCustom(AccessPath path, ITypeAccessorEmitter emitter, ILGenerator il, int index,
        string key, Type type, LocalBuilder? handlerValues, int specialHandlerStart, int boolConditionStart) {
        if (path == AccessPath.Direct)
            emitter.Emit(il, index, key, type, handlerValues, index - specialHandlerStart,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart);
        else
            emitter.EmitUseWith(il, index, type, index < boolConditionStart);
    }

    private static void EmitDefault(AccessPath path, ILGenerator il, int index, string key,
        ParameterMemberAccess member, SpecialHandler[] handlers, int specialHandlerStart,
        int boolConditionStart, LocalBuilder? handlerValues) {
        int handlerIndex = index - specialHandlerStart;
        Action<ILGenerator, ParameterMemberAccess> condition;
        if (!member.IsNested && index >= specialHandlerStart && handlerIndex >= 0 && handlerIndex < handlers.Length
            && handlers[handlerIndex].GetUsageEmitter(member.RootType, member.Member) is { } specialUsage)
            condition = (x, _) => specialUsage(x);
        else
            condition = static (x, m) => EmitDefaultUsage(x, m);

        if (path == AccessPath.Direct)
            AccessorEmitter.EmitSlot(il, index, key, member, handlerValues, handlerIndex,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart,
                condition, static (x, m) => m.EmitValue(x));
        else
            AccessorEmitter.EmitUseWithSlot(il, index, index < boolConditionStart, member,
                condition, static (x, m) => m.EmitValue(x));
    }

    private static void EmitDictionarySlot(ILGenerator il, AccessPath path, int index, string key,
        DictionaryParameterAccess dictionary, int specialHandlerStart, int boolConditionStart,
        LocalBuilder? handlerValues) {
        if (path == AccessPath.UseWith)
            AccessorEmitter.ClearUseWithSlot(il, index);

        Label skip = il.DefineLabel();
        LocalBuilder value = dictionary.EmitTryGet(il, skip);
        DictionaryParameterAccess.EmitDefaultUsage(il, value, dictionary.ValueType);
        il.Emit(OpCodes.Brfalse, skip);

        if (path == AccessPath.Direct)
            AccessorEmitter.EmitDirectValue(il, index, key, handlerValues, index - specialHandlerStart,
                index >= specialHandlerStart && index < boolConditionStart, index < boolConditionStart,
                x => DictionaryParameterAccess.EmitBoxedValue(x, value, dictionary.ValueType));
        else
            AccessorEmitter.EmitUseWithValue(il, index, index < boolConditionStart,
                x => DictionaryParameterAccess.EmitBoxedValue(x, value, dictionary.ValueType));
        il.MarkLabel(skip);
    }

    private static Type? GetStackType(StackAccessor accessor, int index) {
        ParameterSource? source = accessor.Plan.Sources[index];
        if (source is MemberSource memberSource) {
            ParameterMemberAccess member = memberSource.Access;
            IAccessorEmitter? emitter = GetMemberEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper, member);
            if (emitter is null)
                return member.MemberType;
            if (member.IsNested) {
                if (emitter is not IPathAccessorEmitter pathEmitter)
                    throw NestedEmitterError(emitter, member);
                pathEmitter.Validate(member);
                return pathEmitter.GetStackType(member);
            }
            emitter.Validate(member.RootType, member.Member);
            return emitter.GetStackType(member.RootType, member.Member);
        }
        if (source is DictionarySource dictionarySource)
            return dictionarySource.Access.ValueType;
        if (GetTypeEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper) is { } typeEmitter) {
            typeEmitter.Validate(accessor.Type);
            return typeEmitter.GetStackType(accessor.Type);
        }
        return null;
    }

    private static void EmitStackUsage(StackAccessor accessor, StackAccessorEmission emission, int index) {
        ILGenerator il = emission.IL;
        ParameterSource? source = accessor.Plan.Sources[index];
        if (source is MemberSource memberSource) {
            ParameterMemberAccess member = memberSource.Access;
            Label missing = il.DefineLabel();
            Label end = il.DefineLabel();
            ParameterMemberAccess prepared = member.IsNested ? member.Prepare(il, missing) : member;
            emission.PreparedMembers[index] = prepared;
            IAccessorEmitter? emitter = GetMemberEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper, member);
            if (emitter is null) {
                int handlerIndex = index - accessor.SpecialHandlerStart;
                if (!member.IsNested && index >= accessor.SpecialHandlerStart && handlerIndex >= 0
                    && handlerIndex < accessor.Handlers.Length
                    && accessor.Handlers[handlerIndex].GetUsageEmitter(member.RootType, member.Member) is { } specialUsage)
                    specialUsage(il);
                else
                    EmitDefaultUsage(il, prepared);
            }
            else if (member.IsNested) {
                if (emitter is not IPathAccessorEmitter pathEmitter)
                    throw NestedEmitterError(emitter, member);
                pathEmitter.Validate(prepared);
                pathEmitter.EmitStackUsage(il, prepared);
            }
            else {
                emitter.Validate(member.RootType, member.Member);
                emitter.EmitStackUsage(il, member.RootType, member.Member);
            }
            il.Emit(OpCodes.Br, end);
            il.MarkLabel(missing);
            il.Emit(OpCodes.Ldc_I4_0);
            il.MarkLabel(end);
            return;
        }

        if (source is DictionarySource dictionarySource) {
            Label missing = il.DefineLabel();
            Label end = il.DefineLabel();
            LocalBuilder value = dictionarySource.Access.EmitTryGet(il, missing);
            emission.DictionaryValues[index] = value;
            DictionaryParameterAccess.EmitDefaultUsage(il, value, dictionarySource.Access.ValueType);
            il.Emit(OpCodes.Br, end);
            il.MarkLabel(missing);
            il.Emit(OpCodes.Ldc_I4_0);
            il.MarkLabel(end);
            return;
        }

        if (GetTypeEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper) is { } typeEmitter) {
            typeEmitter.Validate(accessor.Type);
            typeEmitter.EmitStackUsage(il, accessor.Type);
            return;
        }

        il.Emit(OpCodes.Ldc_I4_0);
    }

    private static void EmitStackValue(StackAccessor accessor, StackAccessorEmission emission, int index) {
        ParameterSource? source = accessor.Plan.Sources[index];
        ILGenerator il = emission.IL;
        if (source is MemberSource memberSource) {
            ParameterMemberAccess original = memberSource.Access;
            ParameterMemberAccess member = emission.PreparedMembers[index] ?? original;
            IAccessorEmitter? emitter = GetMemberEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper, original);
            if (emitter is null) {
                member.EmitLoad(il);
                return;
            }
            if (original.IsNested) {
                if (emitter is not IPathAccessorEmitter pathEmitter)
                    throw NestedEmitterError(emitter, original);
                pathEmitter.EmitStackValue(il, member);
                return;
            }
            emitter.EmitStackValue(il, original.RootType, original.Member);
            return;
        }

        if (source is DictionarySource) {
            LocalBuilder value = emission.DictionaryValues[index]
                ?? throw new InvalidOperationException("EmitUsage must be called before EmitValue for a dictionary parameter source.");
            il.Emit(OpCodes.Ldloc, value);
            return;
        }

        if (GetTypeEmitter(accessor.Plan, index, accessor.Type, accessor.Mapper) is { } typeEmitter) {
            typeEmitter.EmitStackValue(il, accessor.Type);
            return;
        }

        throw new InvalidOperationException($"Parameter '{accessor.Mapper.Keys[index]}' has no source on '{accessor.Type}'.");
    }

    private static InvalidOperationException NestedEmitterError(IAccessorEmitter emitter, ParameterMemberAccess member)
        => new($"Parameter rule '{emitter.GetType()}' on nested member '{member.Member.Name}' does not support nested parameter paths. " +
            $"Implement {nameof(IPathAccessorEmitter)} or derive from {nameof(PathAccessorEmitterBase)}.");

    private static void EmitDefaultUsage(ILGenerator il, ParameterMemberAccess member) {
        Type memberType = member.MemberType;
        if (!memberType.IsValueType) {
            member.EmitLoad(il);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }
        if (Nullable.GetUnderlyingType(memberType) is not null) {
            member.EmitLoad(il);
            LocalBuilder value = il.DeclareLocal(memberType);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, memberType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            return;
        }
        il.Emit(OpCodes.Ldc_I4_1);
    }

    private static bool IsReadableParameterMember(MemberInfo member) => member switch {
        FieldInfo => true,
        PropertyInfo property => property.GetMethod is not null && property.GetIndexParameters().Length == 0,
        _ => false
    };

    private static bool IsStatic(MemberInfo member) => member switch {
        FieldInfo field => field.IsStatic,
        PropertyInfo property => property.GetMethod?.IsStatic == true,
        _ => false
    };

    private static MemberInfo[] Append(MemberInfo[] path, MemberInfo member) {
        var result = new MemberInfo[path.Length + 1];
        Array.Copy(path, result, path.Length);
        result[^1] = member;
        return result;
    }


    private static ParameterConflictBehavior GetConflictBehavior(Type type)
        => type.GetCustomAttribute<ParameterConflictAttribute>(inherit: true)?.Behavior
            ?? ParameterConflictBehavior.Throw;

    private static char GetVariablePrefix(Mapper mapper) {
        for (int i = 0; i < mapper.Count; i++) {
            string key = mapper.Keys[i];
            if (key.Length == 0) continue;
            char c = key[0];
            if (!char.IsLetterOrDigit(c) && c != '_') return c;
        }
        return default;
    }

    private static string NormalizeName(string name, char variablePrefix) {
        if (name.Length == 0) return name;
        char first = name[0];
        if (variablePrefix != default) return first == variablePrefix ? name[1..] : name;
        return !char.IsLetterOrDigit(first) && first != '_' ? name[1..] : name;
    }

    private static string GetLogicalName(string key, char variablePrefix)
        => variablePrefix != default && key.Length != 0 && key[0] == variablePrefix ? key[1..] : key;

    internal sealed record AccessPlan(ParameterSource?[] Sources, AccessorEmitterHandler[] TypeHandlers, char VariablePrefix);

    internal abstract class ParameterSource(int depth, byte kindPriority) {
        internal int Depth { get; } = depth;
        internal byte KindPriority { get; } = kindPriority;
        internal abstract string Description { get; }
        internal abstract bool SameSource(ParameterSource other);
    }

    private sealed class MemberSource(ParameterMemberAccess access, bool explicitName)
        : ParameterSource(access.Depth, explicitName ? (byte)0 : (byte)1) {
        internal ParameterMemberAccess Access { get; } = access;
        internal override string Description => explicitName
            ? $"explicit mapping from member '{Access.Member.Name}'"
            : $"member '{Access.Member.Name}'";
        internal override bool SameSource(ParameterSource other)
            => other is MemberSource member && Access.SamePath(member.Access);
    }

    private sealed class DictionarySource(DictionaryParameterAccess access) : ParameterSource(access.Depth, 2) {
        internal DictionaryParameterAccess Access { get; } = access;
        internal override string Description => $"dictionary key '{Access.Key}'";
        internal override bool SameSource(ParameterSource other)
            => ReferenceEquals(this, other);
    }

    private enum AccessPath : byte { Direct, UseWith }
}
