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
        if (info is not ICanUpdateAltNames i)
            return false;
        i.UpdateAltName(modifier);
        return true;
    }
    /// <summary>
    /// Configures the null-value response behavior for parameters matching <paramref name="defaultName"/>.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="invalidOnNull">Wether or not the parameter should be invalid when null</param>
    public static bool SetInvalidOnNull(this TypeParsingInfo info, string defaultName, bool invalidOnNull) {
        if (info is not ICanSetInvalidOnNull i)
            return false;
        i.SetInvalidOnNull(defaultName, invalidOnNull);
        return true;
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
/// <summary></summary>
public interface ICanSetInvalidOnNull {
    /// <summary>
    /// Configures the null-value response behavior for parameters matching <paramref name="defaultName"/>.
    /// </summary>
    /// <param name="defaultName">The parameter name in C#.</param>
    /// <param name="invalidOnNull">Wether or not the parameter should be invalid when null</param>
    public void SetInvalidOnNull(string defaultName, bool invalidOnNull);
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