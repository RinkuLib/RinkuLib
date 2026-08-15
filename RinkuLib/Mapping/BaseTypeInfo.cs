using System.Reflection;
using Rinku.Internal;
using Rinku.Mapping.Defaults;

namespace Rinku.Mapping;

/// <summary>Maps a scalar to the first unused compatible column and applies the standard conversions.</summary>
public class BaseTypeInfo : ScalarTypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly BaseTypeInfo Instance = new();
    private BaseTypeInfo() {}
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (!TargetType.IsBaseType() && !TargetType.IsEnum)
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, "Only supports base types or enums");
    }
    /// <inheritdoc/>
    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter,
        ColumnInfo column, int ordinal) {
        ITypeConverter? converter;
        bool found = parameter.RequireExactType
            ? ITypeConverter.TryGetExactConverter(column.Type, targetType, out converter)
            : ITypeConverter.TryGetConverter(column.Type, targetType, out converter);
        if (!found)
            return null;
        return new ConvertedScalarPlan(parentType, converter!, parameter.NameComparer.GetDefaultName(),
            parameter.NullColHandler, ordinal);
    }
}
/// <summary>Marks the constructor used by <see cref="CtorTypeInfo"/>.</summary>
[AttributeUsage(AttributeTargets.Constructor)]
public class DbConstructorAttribute : Attribute { }
/// <summary>Maps constructor parameters by column order and type instead of by name.</summary>
public class CtorTypeInfo : TypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly CtorTypeInfo Instance;
    static CtorTypeInfo() {
        Instance = new();
    }
    private CtorTypeInfo() { }
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type targetType) {
        if (!targetType.GetConstructors().Any(c => c.GetParameters().Length > 0)) 
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, $"Type {targetType.Name} must have at least one constructor with parameters");
    }
    internal static readonly ParamInfo InfoNullable = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoNotNullable = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoSkip = new(ParamInfo.NoType, AbortOnNullAndNotNullHandle.Instance, NoNameComparer.Instance);
    /// <inheritdoc/>
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
        RegisterGenericArguments(currentClosedType, callerFlags);
        if (!previousUsages.CanContinue(currentClosedType, colUsage.NbUsed, out previousUsages))
            return null;
        var ctors = currentClosedType.GetConstructors();
        ConstructorInfo? ctor = null;
        for (int i = 0; i < ctors.Length; i++) {
            var c = ctors[i];
            if (c.GetCustomAttribute<DbConstructorAttribute>() is not null) {
                ctor = c;
                break;
            }
            if (ctor is null && c.GetParameters().Length != 0)
                ctor = c;
        }
        if (ctor is null)
            return null;
        Span<bool> checkpoint = stackalloc bool[colUsage.Length];
        colUsage.InitCheckpoint(checkpoint, out var lastIndUsed, out var nbClaims);
        var parameters = ctor.GetParameters();
        var readers = new DbItemPlan[parameters.Length];
        colModifier.Flags |= UsageFlags.SequentialRead;
        for (int i = 0; i < readers.Length; i++) {
            var type = parameters[i].ParameterType;
            var info = ForceGet(type);
            var itemParamInfo = info is IMultiRowTypeParsingInfo ? InfoSkip
                : !type.IsValueType || Nullable.GetUnderlyingType(type) is not null ? InfoNullable : InfoNotNullable;
            var r = info.TryGetParser(type, previousUsages, itemParamInfo, columns, colModifier, ref colUsage);
            if (r is null) {
                colUsage.Rollback(checkpoint, lastIndUsed, nbClaims);
                return null;
            }
            readers[i] = r;
        }
        return new CustomClassParser(previousUsages.LatestUsedType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, ctor, [.. readers]);
    }
}
