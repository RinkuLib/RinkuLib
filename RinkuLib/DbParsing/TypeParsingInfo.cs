using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;

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
/// if a specific version isn't registered. All lookups automatically unwrap <see cref="Nullable{T}"/>.
/// 
/// </remarks>
public abstract class TypeParsingInfo {
    /// <summary>Identify if the instance can actualy handle the <see cref="Type"/> of <paramref name="TargetType"/></summary>
    public abstract void ValidateCanUseType(Type TargetType);
    static TypeParsingInfo() {
        AddOrSet(typeof(ValueTuple<>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,,>), TupleTypeinfo.Instance);
        AddOrSet(typeof(ValueTuple<,,,,,,,>), TupleTypeinfo.Instance);
        AddOrSet<DynaObject>(DynaObjectTypeInfo.Instance);
        AddOrSet(typeof(NotNull<>), NotNullTypeInfo.Instance);
    }
    /// <summary>
    /// Global cache of type metadata. Access is managed through static methods 
    /// to ensure thread-safety and proper initialization.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, TypeParsingInfo> TypeInfos = [];
    //private IDbTypeParserInfoMatcher? _matcher;
    /*
    /// <summary>
    /// A custom injection point that allows developers to replace the default matching logic.
    /// If provided, this implementation takes full control over the Negotiation Phase for this type.
    /// </summary>
    public IDbTypeParserInfoMatcher? Matcher { get {
            if (!IsInit)
                Init();
            return _matcher;
        } set {
            if (value is null || !value.CanUseType(Type))
                throw new InvalidOperationException($"the Matcher must be of type {Type}");
            Interlocked.Exchange(ref _matcher, value);
        }
    }*/
    /// <summary>
    /// Checks if a type is supported for mapping. 
    /// Automatically unwraps <see cref="Nullable{T}"/> to evaluate the underlying type.
    /// </summary>
    public static bool IsUsableType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericParameter || type.IsBaseType() || type.IsEnum)
            return true;
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
    /// <item>Unwraps <see cref="Nullable{T}"/>.</item>
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
    /// <item>Unwraps <see cref="Nullable{T}"/>.</item>
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
    /// Maps a property name to a specific database column alias.
    /// </summary>
    /// <param name="defaultName">The member name in C#.</param>
    /// <param name="nameToAdd">The alternative name to add to the member.</param>
    public virtual void AddAltName(string defaultName, string nameToAdd)
        => throw new NotImplementedException();
    /// <summary>
    /// Configures the null-value response behavior for parameters matching <paramref name="defaultName"/>.
    /// </summary>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="invalidOnNull">Wether or not the parameter should be invalid when null</param>
    public virtual void SetInvalidOnNull(string defaultName, bool invalidOnNull)
        => throw new NotImplementedException();
    /// <summary>
    /// Mannualy add a possible construction path that will be prioritized as much as possible
    /// </summary>
    public virtual void AddPossibleConstruction(MethodBase methodBase)
        => throw new NotImplementedException();
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
    public abstract DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage);
}