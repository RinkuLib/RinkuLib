using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// Class provide a default way to "match" the parameter when the schema negociation fail.
/// Wraps the <see cref="INullColHandler"/> to reduce memory usage since normaly no default are provided.
/// </summary>
public abstract class MayProvideDefaultValue(INullColHandler nullColHandler) : INullColHandler {
    /// <summary>The caller to get the item parser on fail to match with schema</summary>
    public abstract DbItemParser? TryGetItemParser(Type type);
    /// <summary>The <see cref="INullColHandler"/> thet will be use when a normal match with the schema occurs</summary>
    public INullColHandler NullColHandler { get => field; set => field = Interlocked.Exchange(ref field, value); } = nullColHandler;
    /// <inheritdoc/>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint)
        => NullColHandler.HandleNull(closedType, paramName, generator, nullSetPoint);
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType)
        => NullColHandler.IsBr_S(closedType);
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType)
        => NullColHandler.NeedNullJumpSetPoint(closedType);
    /// <inheritdoc/>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) {
        NullColHandler = NullColHandler.SetInvalidOnNull(type, invalidOnNull);
        return this;
    }
}
/// <summary>
/// Generate the IL of the default value of the type
/// </summary>
public class DefaultEmiter(Type targetType) : DbItemParser {
    private readonly Type targetType = targetType;
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint)
        => EmitDefaultValue(targetType, generator);
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) => true;
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
}
/// <summary>
/// Emmit the default value of the type when no match with the schema
/// </summary>
public class DefaultValueProvider(INullColHandler nullColHandler) : MayProvideDefaultValue(nullColHandler) {
    /// <inheritdoc/>
    public override DbItemParser? TryGetItemParser(Type type) 
        => new DefaultEmiter(type);
}