using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary></summary>
public class CollectionParsingInfo : TypeParsingInfo {
    /// <summary></summary>
    public static readonly CollectionParsingInfo Instance = new();
    /// <inheritdoc/>
    public override void ValidateCanUseType(Type TargetType) { }
    /// <inheritdoc/>
    public override DbItemParser? TryGetParser(Type parentType, Type currentClosedType, ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage) {
        if (!currentClosedType.IsArray)
            return null;
        var type = parentType.GetElementType()!;
        if (!TryGetInfo(type, out var typeInfo))
            return null;
        colModifier = colModifier.Add(paramInfo.NameComparer);
        var node = typeInfo.TryGetParser(currentClosedType, type, NullableTransientParamInfo, columns, colModifier, ref colUsage);
        if (node is null)
            return null;
        return new CollectionDbParser(node);
        
    }
}
/// <summary></summary>
public class CollectionDbParser(DbItemParser node) : DbItemParser {
    private readonly DbItemParser node = node;
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject) {
        throw new NotImplementedException();
    }
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex)
        => false;
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
}
