using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>Direcly match to the first unused column with the matching type</summary>
public class BaseTypeInfo : TypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly BaseTypeInfo Instance = new();
    private BaseTypeInfo() {}
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (!TargetType.IsBaseType() && !TargetType.IsEnum)
            throw new InvalidOperationException($"Only supports base types or enums");
    }
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        int i = 0;
        ITypeConverter? converter = null;
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
                    if (!paramInfo.NameComparer.Match(column.Name, colModifier.Comparers) || !ITypeConverter.TryGetConverter(column.Type, currentClosedType, out converter))
                        i = columns.Length;
                }
            }
        }
        else {
            for (; i < columns.Length; i++) {
                if (!canReuse && colUsage.IsUsed(i))
                    continue;
                var column = columns[i];
                if (paramInfo.NameComparer.Match(column.Name, colModifier.Comparers) && ITypeConverter.TryGetConverter(column.Type, currentClosedType, out converter))
                    break;
            }
        }
        if (i >= columns.Length || converter is null)
            return paramInfo.FallbackTryGetParser(currentClosedType);
        colUsage.Use(i);
        return new BasicParser(previousUsages.LatestUsedType, converter, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, i);
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
            throw new InvalidOperationException($"Type {targetType.Name} must have at least one constructor with parameters.");
    }
    internal static readonly ParamInfo InfoNullable = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoNotNullable = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
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
        var readers = new DbItemParser[parameters.Length];
        colModifier.Flags |= UsageFlags.SequentialRead;
        for (int i = 0; i < readers.Length; i++) {
            var type = parameters[i].ParameterType;
            var itemParamInfo = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null ? InfoNullable : InfoNotNullable;
            var r = ForceGet(type).TryGetParser(type, previousUsages, itemParamInfo, columns, colModifier, ref colUsage);
            if (r is null) {
                colUsage.Rollback(checkpoint, lastIndUsed);
                return null;
            }
            readers[i] = r;
        }
        return new CustomClassParser(previousUsages.LatestUsedType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, ctor, [.. readers]);
    }
}