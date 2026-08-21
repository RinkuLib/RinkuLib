using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Mapping.Defaults;

/// <summary>
/// Describes how to create an object and fill its members from a row.
/// Use it when composing a custom object mapping from smaller plans.
/// </summary>
public class CustomClassParser(Type ParentType, Type Type, string ParamName, INullColHandler NullColHandler, MemberInfo MethodBase, List<DbItemPlan> Parameters, List<(MemberInfo, DbItemPlan)>? Members = null) : SimpleDbItemParser, ICompositeDbItemPlan {
    private readonly Type ParentType = ParentType;
    private readonly Type Type = Type;
    private readonly string ParamName = ParamName;
    private readonly INullColHandler NullColHandler = NullColHandler;
    private readonly MemberInfo MethodBase = MethodBase;
    private readonly List<DbItemPlan> Readers = Parameters;
    private readonly List<(MemberInfo, DbItemPlan)> Members = Members ?? EmptyMembers;
    private static readonly List<(MemberInfo, DbItemPlan)> EmptyMembers = [];
    internal MethodBase Construction => (MethodBase)MethodBase;
    internal IReadOnlyList<DbItemPlan> ConstructorArguments => Readers;
    internal IReadOnlyList<(MemberInfo Member, DbItemPlan Plan)> PostMembers => Members;
    internal Type ResultType => Type;
    internal IReadOnlyList<IGroupingRule> GroupingRules { get; init; } = [];
    internal ColModifier Context { get; init; }
    Type ICompositeDbItemPlan.ResultType => Type;
    MethodBase ICompositeDbItemPlan.Construction => (MethodBase)MethodBase;
    IReadOnlyList<DbItemPlan> ICompositeDbItemPlan.ConstructorArguments => Readers;
    IReadOnlyList<(MemberInfo Member, DbItemPlan Plan)> ICompositeDbItemPlan.PostMembers => Members;
    IReadOnlyList<IGroupingRule> ICompositeDbItemPlan.GroupingRules => GroupingRules;
    ColModifier ICompositeDbItemPlan.Context => Context;
    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => NullColHandler.NeedNullJumpSetPoint(Type);
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
    public override IEnumerable<DbItemPlan> Children {
        get {
            foreach (var reader in Readers)
                yield return reader;
            foreach (var (_, reader) in Members)
                yield return reader;
        }
    }
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        Label? jump = null;
        var localSetPoint = NeedNullSetPoint(cols) ? nullSetPoint : default;
        for (int i = 0; i < Readers.Count; i++) {
            var reader = Readers[i];
            if (!localSetPoint.HasValue && reader.NeedNullSetPoint(cols)) {
                jump = generator.DefineLabel();
                localSetPoint = new(jump.Value, 0);
            }
            ((ISimpleDbItemPlan)reader).Emit(cols, generator, localSetPoint.WithItemOnStack(i));
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
        Label? endLabel = NullColHandler.HandleNull(ParentType, Type, ParamName, generator, nullSetPoint);
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
            ((ISimpleDbItemPlan)reader).Emit(cols, generator, l.HasValue ? new(l.Value, 1) : default);
            EmitMemberDispatch(generator, member);
            if (l.HasValue)
                generator.MarkLabel(l.Value);
        }
        generator.Emit(OpCodes.Ldloc, instanceLocal);
    }
}
