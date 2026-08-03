using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// A composite parser that builds complex types and fills their members.
/// It coordinates the evaluation stack to satisfy constructor parameters and setter methods.
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
    /// <summary>The constructor or factory this node builds its instance with, what the multi-row emit calls.</summary>
    internal MethodBase Construction => (MethodBase)MethodBase;
    /// <summary>The construction arguments in order, each a plan for one constructor or factory parameter.</summary>
    internal IReadOnlyList<DbItemPlan> ConstructorArguments => Readers;
    /// <summary>The members filled after construction, each a target and its plan.</summary>
    internal IReadOnlyList<(MemberInfo Member, DbItemPlan Plan)> PostMembers => Members;
    /// <summary>The type this node builds.</summary>
    internal Type ResultType => Type;
    /// <summary>The group boundary key of this node's type, captured in pass 1 and negotiated by the multi-row emit.</summary>
    internal IGroupingRule? GroupKey { get; init; }
    /// <summary>The name-matching context this node negotiated in, so the key can match its columns the same way.</summary>
    internal ColModifier Context { get; init; }
    Type ICompositeDbItemPlan.ResultType => Type;
    MethodBase ICompositeDbItemPlan.Construction => (MethodBase)MethodBase;
    IReadOnlyList<DbItemPlan> ICompositeDbItemPlan.ConstructorArguments => Readers;
    IReadOnlyList<(MemberInfo Member, DbItemPlan Plan)> ICompositeDbItemPlan.PostMembers => Members;
    IGroupingRule? ICompositeDbItemPlan.GroupKey => GroupKey;
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
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject) {
        Label? jump = null;
        var localSetPoint = NeedNullSetPoint(cols) ? nullSetPoint : default;
        targetObject = null;
        for (int i = 0; i < Readers.Count; i++) {
            var reader = Readers[i];
            if (!localSetPoint.HasValue && reader.NeedNullSetPoint(cols)) {
                jump = generator.DefineLabel();
                localSetPoint = new(jump.Value, 0);
            }
            ((ISimpleDbItemPlan)reader).Emit(cols, generator, localSetPoint.WithItemOnStack(i), out targetObject);
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
            ((ISimpleDbItemPlan)reader).Emit(cols, generator, l.HasValue ? new(l.Value, 1) : default, out _);
            EmitMemberDispatch(generator, member);
            if (l.HasValue)
                generator.MarkLabel(l.Value);
        }
        generator.Emit(OpCodes.Ldloc, instanceLocal);
    }
}
