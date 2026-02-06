using System.Reflection.Emit;

namespace RinkuLib.DbParsing;

public abstract class MayProvideDefaultValue(INullColHandler nullColHandler) : INullColHandler {
    public abstract DbItemParser? TryGetItemParser(Type type);
    public INullColHandler NullColHandler { get => field; set => field = Interlocked.Exchange(ref field, value); } = nullColHandler;
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint)
        => NullColHandler.HandleNull(closedType, paramName, generator, nullSetPoint);
    public bool IsBr_S(Type closedType)
        => NullColHandler.IsBr_S(closedType);
    public bool NeedNullJumpSetPoint(Type closedType)
        => NullColHandler.NeedNullJumpSetPoint(closedType);
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) {
        NullColHandler = NullColHandler.SetInvalidOnNull(type, invalidOnNull);
        return this;
    }
}
public class DefaultEmiter(Type targetType) : DbItemParser {
    private readonly Type targetType = targetType;
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint)
        => EmitDefaultValue(targetType, generator);
    public override bool IsSequencial(ref int previousIndex) => true;
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
}
public class DefaultValueProvider(INullColHandler nullColHandler) : MayProvideDefaultValue(nullColHandler) {
    public override DbItemParser? TryGetItemParser(Type type) 
        => new DefaultEmiter(type);
}