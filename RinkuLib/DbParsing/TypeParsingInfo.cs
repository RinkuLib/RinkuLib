using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary>Marks type metadata whose value is folded across multiple result rows.</summary>
public interface IMultiRowTypeParsingInfo;
/// <summary>
/// How a type is read, its constructors and factory methods to try and its members to fill, the recipe the
/// object parser follows. Register or refine one to change how a type maps, add an alternative name, pin a
/// construction path, or adjust null handling, once and for the whole app. Registration is by exact type, and
/// an open generic definition covers every closed form that has no entry of its own.
/// </summary>
public abstract class TypeParsingInfo {
    private static int ConfigurationVersion;
    internal static int CurrentConfigurationVersion => Volatile.Read(ref ConfigurationVersion);
    internal static void TouchConfiguration() => Interlocked.Increment(ref ConfigurationVersion);
    internal static readonly ParamInfo NullableTransientParamInfo = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo NotNullTransientParamInfo = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <summary>Identify if the instance can actualy handle the <see cref="Type"/> of <paramref name="TargetType"/></summary>
    public abstract void ValidateCanUseType(Type TargetType);
    static TypeParsingInfo() {
        if (CtorTypeInfo.Instance is { } tuples)
            RegisterTuples(tuples);
        if (DynaObjectTypeInfo.Instance is { } dyna)
            RegisterDynaObject(dyna);
        AddOrSet(typeof(List<>), MultiRowTypeParsingInfo.ForList);
        AddOrSet(typeof(IEnumerable<>), MultiRowTypeParsingInfo.ForList);
    }
    internal static void RegisterTuples(CtorTypeInfo instance) {
        AddOrSet(typeof(ValueTuple<>), instance);
        AddOrSet(typeof(ValueTuple<,>), instance);
        AddOrSet(typeof(ValueTuple<,,>), instance);
        AddOrSet(typeof(ValueTuple<,,,>), instance);
        AddOrSet(typeof(ValueTuple<,,,,>), instance);
        AddOrSet(typeof(ValueTuple<,,,,,>), instance);
        AddOrSet(typeof(ValueTuple<,,,,,,>), instance);
        AddOrSet(typeof(ValueTuple<,,,,,,,>), instance);
    }
    internal static void RegisterDynaObject(DynaObjectTypeInfo instance)
        => AddOrSet<DynaObject>(instance);
    /// <summary>
    /// Global cache of type metadata. Access is managed through static methods 
    /// to ensure thread-safety and proper initialization.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, TypeParsingInfo> TypeInfos = [];
    /// <summary>
    /// Checks if a type is supported for mapping. 
    /// Automatically unwraps <see cref="MaybeNull{T}"/> to evaluate the underlying type.
    /// </summary>
    public static bool IsUsableType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericParameter || type.IsBaseType() || type.IsEnum)
            return true;
        if (type.IsArray)
            return IsUsableType(type.GetElementType()!);
        if (TypeInfos.ContainsKey(type))
            return true;
        if (type.IsGenericType && TypeInfos.ContainsKey(type.GetGenericTypeDefinition()))
            return true;
        if (type.IsAssignableTo(typeof(IDbReadable)))
            return true;
        return false;
    }
    /// <summary>
    /// Attempts to retrieve a registry for the specified type.
    /// </summary>
    /// <remarks>
    /// <b>Lookup Logic:</b>
    /// <list type="number">
    /// <item>Unwraps <see cref="MaybeNull{T}"/>.</item>
    /// <item>Returns an exact match if one exists.</item>
    /// <item>If the type is a closed generic and no exact match exists, it attempts to
    ///    return the registry for the <b>Open Generic Type Definition</b>.</item>
    /// <item>If not found and the type implements <see cref="IDbReadable"/>, it registers and 
    /// returns it, defaulting to the <b>Open Generic Type Definition</b> for generics.</item>
    /// </list>
    /// </remarks>
    public static bool TryGetInfo(Type type, [MaybeNullWhen(false)] out TypeParsingInfo typeInfo) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out typeInfo))
            return true;
        if (type.IsBaseType() || type.IsEnum) {
            typeInfo = TypeInfos.GetOrAdd(type, BaseTypeInfo.Instance);
            return true;
        }
        if (type.IsSZArray) {
            typeInfo = TypeInfos.GetOrAdd(type, MultiRowTypeParsingInfo.ForArray);
            return true;
        }
        if (type.IsGenericType) {
            type = type.GetGenericTypeDefinition();
            if (TypeInfos.TryGetValue(type, out typeInfo))
                return true;
        }
        if (!type.IsAssignableTo(typeof(IDbReadable)))
            return false;
        typeInfo = TypeInfos.GetOrAdd(type, new DefaultTypeParsingInfo(type));
        return true;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo ForceGet(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (!type.IsGenericType) {
            infos = type.IsBaseType() || type.IsEnum ? BaseTypeInfo.Instance
                : type.IsSZArray ? MultiRowTypeParsingInfo.ForArray
                : new DefaultTypeParsingInfo(type);
            return TypeInfos.GetOrAdd(type, infos);
        }
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        return TypeInfos.GetOrAdd(type, new DefaultTypeParsingInfo(type));
    }
    /// <summary>
    /// Performs a prioritized lookup in the global cache.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Unwraps <see cref="MaybeNull{T}"/>.</item>
    /// <item>Returns an exact match if one exists.</item>
    /// <item>If the type is a closed generic and no exact match exists, it attempts to
    ///    return the registry for the <b>Open Generic Type Definition</b>.</item>
    /// </list>
    /// </remarks>
    public static TypeParsingInfo? Get(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (type.IsSZArray && !type.IsBaseType())
            return MultiRowTypeParsingInfo.ForArray;
        if (!type.IsGenericType)
            return null;
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        return null;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo GetOrAdd(Type type, TypeParsingInfo? toUseIfNotPresent = null, bool saveAsGenericDefinitionWhenGeneric = true) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (TypeInfos.TryGetValue(type, out var infos))
            return infos;
        if (!type.IsGenericType || !saveAsGenericDefinitionWhenGeneric) {
            toUseIfNotPresent?.ValidateCanUseType(type);
            infos = toUseIfNotPresent ?? (type.IsBaseType() || type.IsEnum
                ? BaseTypeInfo.Instance : new DefaultTypeParsingInfo(type));
            return TypeInfos.GetOrAdd(type, infos);
        }
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        toUseIfNotPresent?.ValidateCanUseType(type);
        return TypeInfos.GetOrAdd(type, toUseIfNotPresent ?? new DefaultTypeParsingInfo(type));
    }
    /// <summary>
    /// Puts <paramref name="typeParsingInfo"/> in as the info for <paramref name="type"/>, replacing
    /// whatever was there. This is the one entry that overwrites, which is what a caller naming both the
    /// type and its info is asking for, and it wins over a registration a query would have made on its own.
    /// </summary>
    public static void AddOrSet(Type type, TypeParsingInfo typeParsingInfo) {
        typeParsingInfo.ValidateCanUseType(type);
        TypeInfos[type] = typeParsingInfo;
        TouchConfiguration();
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo GetOrAdd<T>(TypeParsingInfo? toUseIfNotPresent = null, bool saveAsGenericDefinitionWhenGeneric = true) => GetOrAdd(typeof(T), toUseIfNotPresent, saveAsGenericDefinitionWhenGeneric);
    /// <inheritdoc cref="AddOrSet(Type, TypeParsingInfo)"/>
    public static void AddOrSet<T>(TypeParsingInfo typeParsingInfo, bool saveAsGenericDefinitionWhenGeneric = true) => AddOrSet(saveAsGenericDefinitionWhenGeneric && typeof(T).IsGenericType ? typeof(T).GetGenericTypeDefinition() : typeof(T), typeParsingInfo);

    /// <summary>
    /// Registers the generic arguments owned by <paramref name="type"/> when the caller's construction
    /// requested parameter registration. This is deliberately one level only. The child that owns the
    /// generic type decides whether and how its own arguments are registered.
    /// </summary>
    protected static void RegisterGenericArguments(Type type, MethodCtorInfo.AdditionalFlags callerFlags) {
        if (!callerFlags.HasFlag(MethodCtorInfo.AdditionalFlags.ParametersAreReadable) || !type.IsGenericType)
            return;
        foreach (var argument in type.GetGenericArguments())
            ForceGet(argument);
    }

    /// <summary>Evaluates a received schema and emits a specialized parser when the schema is supported.</summary>
    /// <param name="currentClosedType">The closed type being parsed.</param>
    /// <param name="previousUsages">The recursion state for the current path.</param>
    /// <param name="paramInfo">The slot rules for the value being parsed.</param>
    /// <param name="columns">The result columns available to the parser.</param>
    /// <param name="colModifier">The current column matching modifiers.</param>
    /// <param name="colUsage">The columns already consumed by the current path.</param>
    /// <param name="callerFlags">Flags from the construction path that requested this child.</param>
    public abstract DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default);
}
/// <summary>Reshapes a registered type's mapping, its alternative names, null rules, construction paths, and members.</summary>
public static class TypeParsingInfoHelper {
    /// <summary>
    /// Adjusts how the type's members are matched to column names, when the info supports it.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="modifier">A delegate that will manage both matching with name comparer and 
    /// updating it (returning null wount change the current comparer)</param>
    public static bool UpdateAltName(this TypeParsingInfo info, Func<INameComparer, INameComparer?> modifier) {
        if (info is ICanUpdateAltNames i) {
            i.UpdateAltName(modifier);
            return true;
        }
        if (info is ICanProvideParamInfos provider) {
            foreach (var p in provider.GetParamInfos())
                p.UpdateAltName(modifier);
            return true;
        }
        return false;
    }
    /// <summary>Replaces the group boundary of an editable info, returning <see langword="false"/> when the info does not expose one.</summary>
    public static bool SetGroupKey(this TypeParsingInfo info, IGroupingRule rule) {
        if (info is not ICanUpdateGroupKey editable)
            return false;
        editable.GroupKey = rule;
        return true;
    }
    /// <summary>Clears the type or construction group boundary, returning false when the info does not expose one.</summary>
    public static bool ClearGroupKey(this TypeParsingInfo info) {
        if (info is not ICanUpdateGroupKey editable)
            return false;
        editable.GroupKey = null;
        return true;
    }
    /// <summary>Sets the type-level group boundary of <typeparamref name="T"/> to <paramref name="rule"/>.</summary>
    public static void SetGroupKey<T>(IGroupingRule rule) => SetOrThrow<T>(rule);
    /// <summary>Clears the type-level group boundary of <typeparamref name="T"/> so inference applies.</summary>
    public static void ClearGroupKey<T>() => ClearOrThrow<T>();
    /// <summary>Gets the construction path with the specified parameter types.</summary>
    public static MethodCtorInfo GetConstruction(this TypeParsingInfo info, params Type[] constructionParameters) {
        if (info is not ICanProvideConstructions provider)
            throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType, $"{info.GetType()} does not expose its constructions");
        foreach (var mci in provider.PossibleConstructors)
            if (SameShape(mci, constructionParameters))
                return mci;
        throw new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable,
            $"no construction takes ({string.Join(", ", constructionParameters.Select(t => t.Name))})");
    }
    /// <summary>Gets the exact constructor or factory method registered in the type info.</summary>
    public static MethodCtorInfo GetConstruction(this TypeParsingInfo info, System.Reflection.MethodBase construction) {
        if (info is not ICanProvideConstructions provider)
            throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType, $"{info.GetType()} does not expose its constructions");
        foreach (var mci in provider.PossibleConstructors)
            if (mci.MethodBase == construction || IsClosedMatch(mci.MethodBase, construction, construction.DeclaringType))
                return mci;
        throw new RinkuConfigurationException(ErrorCodes.ConstructionShapeNotUsable,
            $"{construction} is not a registered construction");
    }
    /// <summary>Gets a construction path of <typeparamref name="T"/> by parameter types.</summary>
    public static MethodCtorInfo GetConstruction<T>(params Type[] constructionParameters)
        => TypeParsingInfo.ForceGet(typeof(T)).GetConstruction(constructionParameters);
    /// <summary>Gets the exact registered construction path of <typeparamref name="T"/>.</summary>
    public static MethodCtorInfo GetConstruction<T>(System.Reflection.MethodBase construction)
        => TypeParsingInfo.ForceGet(typeof(T)).GetConstruction(construction);
    private static bool IsClosedMatch(System.Reflection.MethodBase registered, System.Reflection.MethodBase requested, Type? requestedDeclaringType) {
        if (requestedDeclaringType is null || !requestedDeclaringType.IsGenericType || registered.DeclaringType != requestedDeclaringType.GetGenericTypeDefinition())
            return false;
        return registered.GetClosedMember(requestedDeclaringType) == requested;
    }
    private static bool SameShape(MethodCtorInfo mci, Type[] parameterTypes) {
        if (mci.Parameters.Length != parameterTypes.Length)
            return false;
        for (int i = 0; i < parameterTypes.Length; i++)
            if (mci.Parameters[i].Type != parameterTypes[i])
                return false;
        return true;
    }
    /// <summary>Sets the group boundary of <typeparamref name="T"/> to an equality key over the named members.</summary>
    public static void SetGroupKey<T>(params string[] members) {
        var infos = new System.Reflection.MemberInfo[members.Length];
        for (int i = 0; i < members.Length; i++)
            infos[i] = (System.Reflection.MemberInfo?)typeof(T).GetProperty(members[i]) ?? typeof(T).GetField(members[i])
                ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"{typeof(T)} has no property or field named {members[i]}");
        SetOrThrow<T>(new EqualityGroupingRule(infos));
    }
    /// <summary>Sets the group boundary of <typeparamref name="T"/> to an equality key over the named columns, each read as whatever type its column carries, no member required.</summary>
    public static void SetGroupKeyColumns<T>(params string[] columns)
        => SetOrThrow<T>(new EqualityGroupingRule(columns));
    /// <summary>Sets the group boundary of <typeparamref name="T"/> to the boundary a marked static method computes.</summary>
    public static void SetGroupKeyMethod<T>(string method) {
        var m = typeof(T).GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"{typeof(T)} has no static method named {method}");
        SetOrThrow<T>(new MethodGroupingRule(m));
    }
    private static void SetOrThrow<T>(IGroupingRule rule) {
        if (!TypeParsingInfo.ForceGet(typeof(T)).SetGroupKey(rule))
            throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType, $"{typeof(T)} does not expose an editable group boundary");
    }
    private static void ClearOrThrow<T>() {
        if (!TypeParsingInfo.ForceGet(typeof(T)).ClearGroupKey())
            throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType, $"{typeof(T)} does not expose an editable group boundary");
    }
    /// <summary>
    /// The shared road every null-handling helper travels, the info's own
    /// <see cref="ICanUpdateNullColHandlers"/> capability when it has one, otherwise any info that can
    /// provide its slots via <see cref="ICanProvideParamInfos"/>. Returns <see langword="false"/> only
    /// when neither is available.
    /// </summary>
    private static bool ApplyNullColHandler(TypeParsingInfo info, Func<ParamInfo, INullColHandler?> modifier) {
        if (info is ICanUpdateNullColHandlers i) {
            i.UpdateNullColHandler(modifier);
            return true;
        }
        if (info is ICanProvideParamInfos provider) {
            foreach (var p in provider.GetParamInfos())
                p.NullColHandler = modifier(p) ?? p.NullColHandler;
            return true;
        }
        return false;
    }
    /// <summary>
    /// Sets the null-value response behavior for the slots matching <paramref name="defaultName"/>.
    /// The simplest form of <see cref="UpdateNullColHandler(TypeParsingInfo, Func{ParamInfo, INullColHandler?})"/>.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="handler">The handler the matching slots receive</param>
    public static bool UpdateNullColHandler(this TypeParsingInfo info, string defaultName, INullColHandler handler)
        => ApplyNullColHandler(info, p => p.NameComparer.Contains(defaultName) ? handler : null);
    /// <summary>
    /// Updates the null-value response behavior of the slots. The form that gives full control:
    /// the <paramref name="modifier"/> sees each slot and decides.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="modifier">A delegate that receives each slot and returns its new
    /// <see cref="INullColHandler"/> (returning null wount change the current handler)</param>
    public static bool UpdateNullColHandler(this TypeParsingInfo info, Func<ParamInfo, INullColHandler?> modifier)
        => ApplyNullColHandler(info, modifier);
    /// <summary>
    /// Configures the null-value response behavior for the slots matching <paramref name="defaultName"/>.
    /// The simplest form of <see cref="SetAbortOnNull(TypeParsingInfo, Func{ParamInfo, bool?})"/>.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="abortOnNull">Wether or not the parameter should be aborted when null</param>
    public static bool SetAbortOnNull(this TypeParsingInfo info, string defaultName, bool abortOnNull)
        => ApplyNullColHandler(info, p => p.NameComparer.Contains(defaultName)
            ? p.NullColHandler.SetAbortOnNull(p.Type, abortOnNull) : null);
    /// <summary>
    /// Updates the AbortOnNull behavior of the slots. The form that gives full control:
    /// the <paramref name="modifier"/> sees each slot and decides.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="modifier">A delegate that receives each slot and returns whether it should be
    /// abort when null (returning null leaves the slot as is)</param>
    public static bool SetAbortOnNull(this TypeParsingInfo info, Func<ParamInfo, bool?> modifier)
        => ApplyNullColHandler(info, p => modifier(p) is bool b
            ? p.NullColHandler.SetAbortOnNull(p.Type, b) : null);
    /// <summary>
    /// Manually add a member to fill after construction: an existing <see cref="MemberParser"/>.
    /// </summary>
    public static bool AddMember(this TypeParsingInfo info, MemberParser member) {
        if (info is not ICanAddMember i)
            return false;
        i.AddMember(member);
        return true;
    }
    /// <summary>
    /// Manually add a member to fill after construction, a public field or writable property, or a
    /// setter method (<c>static (instance, value)</c> or instance <c>(value)</c>). The value's
    /// <see cref="ParamInfo"/> is derived the same way discovery derives it.
    /// </summary>
    public static bool AddMember(this TypeParsingInfo info, MemberInfo member) {
        if (info is not ICanAddMember i)
            return false;
        i.AddMember(BuildMemberParser(member));
        return true;
    }
    /// <summary>
    /// Derives the value <see cref="ParamInfo"/> for a member and wraps it in a <see cref="MemberParser"/>,
    /// mirroring how <c>DefaultTypeParsingInfo.Init</c> builds them for fields and properties and
    /// extending it to setter methods.
    /// </summary>
    private static MemberParser BuildMemberParser(MemberInfo member) {
        ParamInfo? param = member switch {
            PropertyInfo prop => ParamInfo.TryNew(prop),
            FieldInfo field => ParamInfo.TryNew(field),
            MethodInfo method => ParamInfo.TryNew(method.GetParameters() is { Length: 2 } ps && method.IsStatic
                ? ps[1] : method.GetParameters()[0]),
            _ => throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"{member} is not a field, property, or setter method")
        };
        if (param is null)
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"The value type of {member} is not a usable type");
        return new MemberParser(member, param);
    }
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public static bool AddPossibleConstruction(this TypeParsingInfo info, MethodBase methodBase) {
        if (info is not ICanAddPossibleConstructor i)
            return false;
        i.AddPossibleConstruction(methodBase);
        return true;
    }
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public static bool AddPossibleConstruction(this TypeParsingInfo info, MethodCtorInfo mci) {
        if (info is not ICanAddPossibleConstructor i)
            return false;
        i.AddPossibleConstruction(mci);
        return true;
    }
}
/// <summary>A type mapping that lets its column-name matching be reshaped.</summary>
public interface ICanUpdateAltNames {
    /// <summary>
    /// Adjusts how each slot is matched to column names.
    /// </summary>
    /// <param name="modifier">A delegate that will manage both matching with name comparer and 
    /// updating it (returning null wount change the current comparer)</param>
    public void UpdateAltName(Func<INameComparer, INameComparer?> modifier);
}
/// <summary>
/// Governs the null handling of an info's own slots. The single primitive every null-handling helper
/// (<c>UpdateNullColHandler</c>, <c>SetAbortOnNull</c>) is derived from: AbortOnNull is just a
/// transform on a slot's <see cref="INullColHandler"/>.
/// </summary>
public interface ICanUpdateNullColHandlers {
    /// <summary>
    /// Updates the null-value response behavior of the slots.
    /// </summary>
    /// <param name="modifier">A delegate that receives each slot and returns its new
    /// <see cref="INullColHandler"/> (returning null wount change the current handler)</param>
    public void UpdateNullColHandler(Func<ParamInfo, INullColHandler?> modifier);
}
/// <summary>A type mapping that can list its slots.</summary>
public interface ICanProvideParamInfos {
    /// <summary>
    /// Enumerates every slot of the type, constructor parameters and members alike.
    /// </summary>
    public IEnumerable<ParamInfo> GetParamInfos();
}
/// <summary>
/// Exposes the whole set of construction paths for reading and wholesale replacement, so callers can
/// reorder or rebuild it without reaching for a concrete info type. Assigning validates every entry.
/// </summary>
public interface ICanProvideConstructions {
    /// <summary>The prioritized construction paths (constructors or static factory methods).</summary>
    public ReadOnlySpan<MethodCtorInfo> PossibleConstructors { get; set; }
}
/// <summary>
/// Exposes the whole set of post-construction members for reading and wholesale replacement, the
/// member counterpart to <see cref="ICanProvideConstructions"/>. Assigning validates every entry.
/// </summary>
public interface ICanProvideMembers {
    /// <summary>The public fields and properties filled after instantiation.</summary>
    public ReadOnlySpan<MemberParser> AvailableMembers { get; set; }
}
/// <summary>A type mapping that lets a post-construction member be added by hand.</summary>
public interface ICanAddMember {
    /// <summary>
    /// Manually add a member to fill after construction, prioritized as it is provided.
    /// </summary>
    public void AddMember(MemberParser member);
}
/// <summary>A type mapping that lets a construction path be added by hand.</summary>
public interface ICanAddPossibleConstructor {
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public void AddPossibleConstruction(MethodBase methodBase)
        => AddPossibleConstruction(new MethodCtorInfo(methodBase));
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public void AddPossibleConstruction(MethodCtorInfo mci);
}
