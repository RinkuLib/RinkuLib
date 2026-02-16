using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// A composite parser responsible for instantiating complex types and populating their members.
/// It coordinates the evaluation stack to satisfy constructor parameters and setter methods.
/// </summary>
public class CustomClassParser(Type Type, string ParamName, INullColHandler NullColHandler, MemberInfo MethodBase, List<DbItemParser> Parameters, List<(MemberInfo, DbItemParser)> Members) : DbItemParser {
    private readonly Type Type = Type;
    private readonly string ParamName = ParamName;
    private readonly INullColHandler NullColHandler = NullColHandler;
    private readonly MemberInfo MethodBase = MethodBase;
    private readonly List<DbItemParser> Readers = Parameters;
    private readonly List<(MemberInfo, DbItemParser)> Members = Members;
    private static readonly List<(MemberInfo, DbItemParser)> EmptyMembers = [];
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => NullColHandler.NeedNullJumpSetPoint(Type);
    /// <summary>Creation without member assignations</summary>
    public CustomClassParser(Type Type, string ParamName, INullColHandler NullColHandler, MemberInfo MethodBase, List<DbItemParser> Parameters)
        : this(Type, ParamName, NullColHandler, MethodBase, Parameters, EmptyMembers) { }
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        for (int i = 0; i < Readers.Count; i++)
            if (!Readers[i].IsSequencial(ref previousIndex))
                return false;
        for (int i = 0; i < Members.Count; i++)
            if (!Members[i].Item2.IsSequencial(ref previousIndex))
                return false;
        return true;
    }
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        Label? jump = null;
        var localSetPoint = nullSetPoint;
        for (int i = 0; i < Readers.Count; i++) {
            var reader = Readers[i];
            if (!localSetPoint.HasValue && reader.NeedNullSetPoint(cols)) {
                jump = generator.DefineLabel();
                localSetPoint = new(jump.Value, 0);
            }
            Readers[i].Emit(cols, generator, localSetPoint.WithItemOnStack(i));
        }
        EmitMemberDispatch(generator, MethodBase);
        var under = Nullable.GetUnderlyingType(Type);
        if (Members.Count > 0)
            ManageMembers(cols, generator, under ?? Type);
        if (under is not null)
            generator.Emit(OpCodes.Newobj, under.GetNullableConstructor());
        Label notNull = generator.DefineLabel();
        var op = OpCodes.Br_S;
        if (!NullColHandler.IsBr_S(Type) || nullSetPoint.NbOfPopToMake + 5 > 127)
            op = OpCodes.Br;
        generator.Emit(op, notNull);
        if (jump.HasValue)
            generator.MarkLabel(jump.Value);
        Label? endLabel = NullColHandler.HandleNull(Type, ParamName, generator, nullSetPoint);
        if (endLabel.HasValue)
            generator.MarkLabel(endLabel.Value);
        generator.MarkLabel(notNull);
    }
    private void ManageMembers(ColumnInfo[] cols, Generator generator, Type type) {
        LocalBuilder instanceLocal = generator.GetLocal(type);
        generator.Emit(OpCodes.Stloc, instanceLocal);
        var opCode = type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc;
        for (int i = 0; i < Members.Count; i++) {
            var (member, reader) = Members[i];
            Label? l = reader.NeedNullSetPoint(cols) ? generator.DefineLabel() : null;
            generator.Emit(opCode, instanceLocal);            
            reader.Emit(cols, generator, l.HasValue ? new(l.Value, 1) : default);
            EmitMemberDispatch(generator, member);
            if (l.HasValue)
                generator.MarkLabel(l.Value);
        }
        generator.Emit(OpCodes.Ldloc, instanceLocal);
    }
}