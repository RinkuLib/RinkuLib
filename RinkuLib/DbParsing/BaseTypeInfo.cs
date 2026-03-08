using System.Runtime.InteropServices;
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
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        int i = 0;
        ITypeConverter? converter = null;
        paramInfo.UpdateColModifier(ref colModifier);
        bool canReuse = colModifier.Flags.HasFlag(UsageFlags.CanReuse);
        if (colModifier.Flags.HasFlag(UsageFlags.SequentialRead) && !colModifier.Flags.HasFlag(UsageFlags.RemoveSequentialRead)) {
            i = colUsage.LastIndexUsed + 1;
            if (i < columns.Length) {
                if (!canReuse && colUsage.IsUsed(i))
                    i = columns.Length;
                else {
                    var column = columns[i];
                    if (!colModifier.Match(column.Name, paramInfo.NameComparer) || !ITypeConverter.TryGetConverter(column.Type, currentClosedType, out converter))
                        i = columns.Length;
                }
            }
        }
        else {
            for (; i < columns.Length; i++) {
                if (!canReuse && colUsage.IsUsed(i))
                    continue;
                var column = columns[i];
                if (colModifier.Match(column.Name, paramInfo.NameComparer) && ITypeConverter.TryGetConverter(column.Type, currentClosedType, out converter))
                    break;
            }
        }
        if (i >= columns.Length || converter is null)
            return paramInfo.FallbackTryGetParser(currentClosedType);
        colUsage.Use(i);
        return new BasicParser(parentType, converter, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, i);
    }
}

/// <summary></summary>
public readonly struct DBPair<TKey, TValue>(TKey Key, TValue Value) {
    /// <summary></summary>
    public readonly TKey Key = Key;
    /// <summary></summary>
    public readonly TValue Value = Value;
}
/// <summary>Handling for tuple that force usage of ite argument types</summary>
public class WrapperTypeInfo<TWrapper> : TypeParsingInfo {
    private readonly static Type GenericDefinition = typeof(TWrapper).GetGenericTypeDefinition();
    /// <summary>Singleton</summary>
    public static readonly WrapperTypeInfo<TWrapper> Instance = new();
    private WrapperTypeInfo() {}
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (!TargetType.IsGenericType || TargetType.GetGenericTypeDefinition() != GenericDefinition)
            throw new InvalidOperationException($"Only supports the {GenericDefinition} types");
    }
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        if (!currentClosedType.IsGenericType || currentClosedType.GetGenericTypeDefinition() != GenericDefinition)
            return null;
        var args = currentClosedType.GetGenericArguments();
        var readers = new List<DbItemParser>(args.Length);
        for (int i = 0; i < args.Length; i++) {
            var type = args[i];
            var r = ForceGet(type).TryGetParser(currentClosedType, type, NotNullTransientParamInfo, columns, colModifier, ref colUsage);
            if (r is null)
                return null;
            readers.Add(r);
        }
        var method = currentClosedType.GetConstructor(args) ?? throw new Exception($"unable to load the ctor for {currentClosedType}");
        return new CustomClassParser(parentType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, method, readers);
    }
}
/// <summary>Handling for tuple that force usage of ite argument types</summary>
public class TupleTypeinfo : TypeParsingInfo {
    /// <summary>Singleton</summary>
    public static readonly TupleTypeinfo Instance = new();
    private TupleTypeinfo() { }
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) {
        if (!TargetType.IsGenericType)
            throw new InvalidOperationException($"Only supports the ValueTuple types");
        TargetType = TargetType.GetGenericTypeDefinition();
        if (TargetType != typeof(ValueTuple<>)
        && TargetType != typeof(ValueTuple<,>)
        && TargetType != typeof(ValueTuple<,,>)
        && TargetType != typeof(ValueTuple<,,,>)
        && TargetType != typeof(ValueTuple<,,,,>)
        && TargetType != typeof(ValueTuple<,,,,,>)
        && TargetType != typeof(ValueTuple<,,,,,,>)
        && TargetType != typeof(ValueTuple<,,,,,,,>))
            throw new InvalidOperationException($"Only supports the ValueTuple types");
    }
    internal static readonly ParamInfo InfoNullable = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoNotNullable = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        if (!currentClosedType.IsGenericType)
            return null;
        var typeArgs = currentClosedType.GetGenericArguments();
        Span<bool> checkpoint = stackalloc bool[colUsage.Length];
        colUsage.InitCheckpoint(checkpoint, out var lastIndUsed);
        var readers = new DbItemParser[typeArgs.Length];
        colModifier.Flags |= UsageFlags.SequentialRead;
        for (int i = 0; i < readers.Length; i++) {
            var type = typeArgs[i];
            var itemParamInfo = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null ? InfoNullable : InfoNotNullable;
            var r = ForceGet(type).TryGetParser(currentClosedType, type, itemParamInfo, columns, colModifier, ref colUsage);
            if (r is null) {
                colUsage.Rollback(checkpoint, lastIndUsed);
                return null;
            }
            readers[i] = r;
        }
        var method = currentClosedType.GetConstructor(typeArgs) ?? throw new Exception($"unable to load the ctor for {currentClosedType}");
        return new CustomClassParser(parentType, currentClosedType, paramInfo.NameComparer.GetDefaultName(), paramInfo.NullColHandler, method, [.. readers]);
    }
}