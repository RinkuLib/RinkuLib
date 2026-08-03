using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>Direcly match to the first unused column with the matching type</summary>
public class BaseTypeInfo : TypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly BaseTypeInfo Instance = new();
    private BaseTypeInfo() {}
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (!TargetType.IsBaseType() && !TargetType.IsEnum
            && !TypeConverterRegistry.HasTarget(TargetType)
            && !DbColumnReaderRegistry.HasValueType(TargetType))
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, "Only supports base types or enums");
    }
    /// <inheritdoc/>
    public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
        int i = 0;
        ITypeConverter? converter = null;
        IColumnReader? columnReader = null;
        paramInfo.UpdateColModifier(ref colModifier);
        var flags = colModifier.Flags;
        if (colModifier.SwapFirstAt >= 0 && colUsage.NbUsed == colModifier.SwapFirstAt)
            flags |= colModifier.SwapFirstFlags;
        bool canReuse = flags.HasFlag(UsageFlags.CanReuse);
        if (flags.HasFlag(UsageFlags.SequentialRead) && !flags.HasFlag(UsageFlags.RemoveSequentialRead)) {
            i = colUsage.LastIndexUsed + 1;
            if (i < columns.Length) {
                if (!canReuse && colUsage.IsUsed(i))
                    i = columns.Length;
                else {
                    var column = columns[i];
                    if (!paramInfo.NameComparer.Match(column.Name, colModifier.Comparers) || !TryGetConverter(column, currentClosedType, paramInfo, out converter, out columnReader))
                        i = columns.Length;
                }
            }
        }
        else {
            for (; i < columns.Length; i++) {
                if (!canReuse && colUsage.IsUsed(i))
                    continue;
                var column = columns[i];
                if (paramInfo.NameComparer.Match(column.Name, colModifier.Comparers) && TryGetConverter(column, currentClosedType, paramInfo, out converter, out columnReader))
                    break;
            }
        }
        if (i >= columns.Length || converter is null)
            return paramInfo.FallbackTryGetParser(currentClosedType);
        colUsage.Use(i);
        return new BasicParser(previousUsages.LatestUsedType, converter, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, i, columnReader);
    }

    private static bool TryGetConverter(ColumnInfo column, Type targetType, ParamInfo paramInfo,
        [MaybeNullWhen(false)] out ITypeConverter converter, out IColumnReader? columnReader) {
        DbColumnReaderRegistry.TryGet(column.Type, out columnReader);
        var sourceType = columnReader?.ValueType ?? column.Type;
        return paramInfo.RequireExactType
            ? ITypeConverter.TryGetExactConverter(sourceType, targetType, out converter)
            : ITypeConverter.TryGetConverter(sourceType, targetType, out converter);
    }
}
/// <summary>
/// When registering a type with <see cref="CtorTypeInfo"/> use this attribute to mark the ctor to use
/// </summary>
[AttributeUsage(AttributeTargets.Constructor)]
public class DbConstructorAttribute : Attribute { }
/// <summary>Handling using a parameterized ctor ignoring parameters names and only considering order and types</summary>
public class CtorTypeInfo : TypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly CtorTypeInfo Instance;
    static CtorTypeInfo() {
        Instance = new();
        RegisterTuples(Instance);
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
        colUsage.InitCheckpoint(checkpoint, out var lastIndUsed);
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
                colUsage.Rollback(checkpoint, lastIndUsed);
                return null;
            }
            readers[i] = r;
        }
        return new CustomClassParser(previousUsages.LatestUsedType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, ctor, [.. readers]);
    }
}
