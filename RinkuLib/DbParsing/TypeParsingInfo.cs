using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;
/// <summary>
/// A metadata registry representing a specific <see cref="Type"/>. 
/// It stores the construction paths and members required to transform a schema into an object.
/// </summary>
/// <remarks>
/// <para><b>I. Instance-Level Configuration:</b></para>
/// This class provides an API to refine mapping behavior, such as adding alternative names 
/// for matching or configuring null-handling recovery. It is also responsible for executing 
/// the resolution logic that produces a parser.
/// 
/// <para><b>II. Static Registry &amp; Generic Fallback:</b></para>
/// Instances are managed through a global cache to ensure metadata consistency. 
/// The registry logic supports <b>Specialization</b>: it can store metadata for a 
/// specific closed generic type, but will fall back to the <b>Open Generic Type Definition</b> 
/// if a specific version isn't registered. All lookups automatically unwrap <see cref="MaybeNull{T}"/>.
/// 
/// </remarks>
public abstract class TypeParsingInfo {
    internal static readonly ParamInfo NullableTransientParamInfo = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo NotNullTransientParamInfo = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <summary>Identify if the instance can actualy handle the <see cref="Type"/> of <paramref name="TargetType"/></summary>
    public abstract void ValidateCanUseType(Type TargetType);
    static TypeParsingInfo() {
        AddOrSet(typeof(ValueTuple<>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,,>), CtorTypeInfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,,,>), CtorTypeInfo.Instance);
        AddOrSet<DynaObject>(DynaObjectTypeInfo.Instance);
    }
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
            typeInfo = BaseTypeInfo.Instance;
            TypeInfos[type] = typeInfo;
            return true;
        }
        if (type.IsGenericType) {
            type = type.GetGenericTypeDefinition();
            if (TypeInfos.TryGetValue(type, out typeInfo))
                return true;
        }
        if (!type.IsAssignableTo(typeof(IDbReadable)))
            return false;
        typeInfo = new DefaultTypeParsingInfo(type);
        TypeInfos[type] = typeInfo;
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
            infos = type.IsBaseType() || type.IsEnum
                ? BaseTypeInfo.Instance : new DefaultTypeParsingInfo(type);
            TypeInfos[type] = infos;
            return infos;
        }
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        infos = new DefaultTypeParsingInfo(type);
        TypeInfos[type] = infos;
        return infos;
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
            TypeInfos[type] = infos;
            return infos;
        }
        type = type.GetGenericTypeDefinition();
        if (TypeInfos.TryGetValue(type, out infos))
            return infos;
        toUseIfNotPresent?.ValidateCanUseType(type);
        infos = toUseIfNotPresent ?? new DefaultTypeParsingInfo(type);
        TypeInfos[type] = infos;
        return infos;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static void AddOrSet(Type type, TypeParsingInfo typeParsingInfo) {
        typeParsingInfo.ValidateCanUseType(type);
        TypeInfos[type] = typeParsingInfo;
    }
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static TypeParsingInfo GetOrAdd<T>(TypeParsingInfo? toUseIfNotPresent = null, bool saveAsGenericDefinitionWhenGeneric = true) => GetOrAdd(typeof(T), toUseIfNotPresent, saveAsGenericDefinitionWhenGeneric);
    /// <summary>
    /// Standard access point to retrieve or create a type's metadata registry.
    /// </summary>
    public static void AddOrSet<T>(TypeParsingInfo typeParsingInfo, bool saveAsGenericDefinitionWhenGeneric = true) => AddOrSet(saveAsGenericDefinitionWhenGeneric && typeof(T).IsGenericType ? typeof(T).GetGenericTypeDefinition() : typeof(T), typeParsingInfo);

    /// <summary>
    /// Evaluates a received schema against the registered metadata to emit a specialized parser.
    /// </summary>
    /// <remarks>
    /// The default logic evaluates <see cref="DefaultTypeParsingInfo.PossibleConstructors"/> and 
    /// <see cref="DefaultTypeParsingInfo.AvailableMembers"/> against the provided <paramref name="columns"/> schema.
    /// </remarks>
    /// <returns>
    /// A configured <see cref="DbItemParser"/> if the schema satisfies a construction path; otherwise, null.
    /// </returns>
    public abstract DbItemParser? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage);
}
/// <summary></summary>
public static class TypeParsingInfoHelper {
    /// <summary>
    /// Maps a property name to a specific database column alias.
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
    /// <summary>
    /// The shared road every null-handling helper travels: the info's own
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
    /// The simplest form of <see cref="SetInvalidOnNull(TypeParsingInfo, Func{ParamInfo, bool?})"/>.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="invalidOnNull">Wether or not the parameter should be invalid when null</param>
    public static bool SetInvalidOnNull(this TypeParsingInfo info, string defaultName, bool invalidOnNull)
        => ApplyNullColHandler(info, p => p.NameComparer.Contains(defaultName)
            ? p.NullColHandler.SetInvalidOnNull(p.Type, invalidOnNull) : null);
    /// <summary>
    /// Updates the invalid-on-null behavior of the slots. The form that gives full control:
    /// the <paramref name="modifier"/> sees each slot and decides.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="modifier">A delegate that receives each slot and returns whether it should be
    /// invalid when null (returning null leaves the slot as is)</param>
    public static bool SetInvalidOnNull(this TypeParsingInfo info, Func<ParamInfo, bool?> modifier)
        => ApplyNullColHandler(info, p => modifier(p) is bool b
            ? p.NullColHandler.SetInvalidOnNull(p.Type, b) : null);
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
    /// Manually add a member to fill after construction: a public field or writable property, or a
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
            _ => throw new ArgumentException($"{member} is not a field, property, or setter method")
        };
        if (param is null)
            throw new ArgumentException($"The value type of {member} is not a usable type");
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
/// <summary></summary>
public interface ICanUpdateAltNames {
    /// <summary>
    /// Maps a property name to a specific database column alias.
    /// </summary>
    /// <param name="modifier">A delegate that will manage both matching with name comparer and 
    /// updating it (returning null wount change the current comparer)</param>
    public void UpdateAltName(Func<INameComparer, INameComparer?> modifier);
}
/// <summary>
/// Governs the null handling of an info's own slots. The single primitive every null-handling helper
/// (<c>UpdateNullColHandler</c>, <c>SetInvalidOnNull</c>) is derived from: invalid-on-null is just a
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
/// <summary></summary>
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
/// <summary></summary>
public interface ICanAddMember {
    /// <summary>
    /// Manually add a member to fill after construction, prioritized as it is provided.
    /// </summary>
    public void AddMember(MemberParser member);
}
/// <summary></summary>
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