using System.Reflection;
using System.Reflection.Emit;
using Rinku;
using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>Thrown when a database <c>NULL</c> is assigned to a value that does not accept null.</summary>
public class NullValueAssignmentException(Type parentType, Type paramType, string paramName) : RinkuReadException(ErrorCodes.NullNotAllowed, $"Constraint Violation: Parameter '{paramName}' of type '{paramType.Name}' is marked as non-nullable, but the source provided a null value. Parent: {parentType}") {
    /// <summary>The name of the parameter that should not be null</summary>
    public readonly string ParameterName = paramName;
    /// <summary>The type of the parent</summary>
    public readonly Type ParentType = parentType;
    /// <summary>The type of the parameter that should not be null</summary>
    public readonly Type ParameterType = paramType;
}
/// <summary>
/// Describes how part of a result is read. Return a plan from
/// <see cref="TypeParsingInfo.TryGetParser(Type, RecursiveInfo, ParamInfo, ColumnInfo[], ColModifier, ref ColumnUsage, MethodCtorInfo.AdditionalFlags)"/>
/// when adding custom type mapping.
/// </summary>
public abstract class DbItemPlan {
    /// <summary>
    /// Returns whether this plan can collapse its owning object when it reads a <c>NULL</c>.
    /// </summary>
    public abstract bool NeedNullSetPoint(ColumnInfo[] cols);
    /// <summary>
    /// Returns whether this plan reads columns in increasing order. Update <paramref name="previousIndex"/>
    /// with the final column used.
    /// </summary>
    public abstract bool IsSequencial(ref int previousIndex);
    /// <summary>
    /// Gets the child plans. Return an empty sequence for a plan with no children.
    /// </summary>
    public virtual IEnumerable<DbItemPlan> Children => [];
    internal static bool AllSimple(DbItemPlan node) {
        if (node is not ISimpleDbItemPlan)
            return false;
        foreach (var child in node.Children)
            if (!AllSimple(child))
                return false;
        return true;
    }
    internal static readonly ConstructorInfo NullAssignmentCtor = typeof(NullValueAssignmentException).GetConstructor([typeof(Type), typeof(Type), typeof(string)])!;
    internal static readonly MethodInfo GetTypeHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!;

    /// <summary>
    /// Writes a throw for <see cref="NullValueAssignmentException"/>. Use it when a target rejects
    /// database <c>NULL</c>.
    /// </summary>
    public static void EmitThrowNullAssignment(Type parentType, Type paramType, string paramName, Generator generator) {
        generator.Emit(OpCodes.Ldtoken, parentType);
        generator.Emit(OpCodes.Call, GetTypeHandle);
        generator.Emit(OpCodes.Ldtoken, paramType);
        generator.Emit(OpCodes.Call, GetTypeHandle);
        generator.Emit(OpCodes.Ldstr, paramName);
        generator.Emit(OpCodes.Newobj, NullAssignmentCtor);
        generator.Emit(OpCodes.Throw);
    }
    /// <summary>
    /// Writes the default value of <paramref name="type"/> onto the evaluation stack.
    /// </summary>
    public static void EmitDefaultValue(Type type, Generator generator) {
        if (!type.IsValueType) {
            generator.Emit(OpCodes.Ldnull);
            return;
        }
        if (type.IsPrimitive || type.IsEnum) {
            if (type == typeof(long) || type == typeof(ulong))
                generator.Emit(OpCodes.Ldc_I8, 0L);
            else if (type == typeof(float))
                generator.Emit(OpCodes.Ldc_R4, 0f);
            else if (type == typeof(double))
                generator.Emit(OpCodes.Ldc_R8, 0d);
            else
                generator.Emit(OpCodes.Ldc_I4_0);
        }
        else {
            LocalBuilder temp = generator.GetLocal(type);
            generator.Emit(OpCodes.Ldloca_S, temp);
            generator.Emit(OpCodes.Initobj, type);
            generator.Emit(OpCodes.Ldloc, temp);
        }
    }
    /// <summary>
    /// Completes a null branch and leaves the default value on the evaluation stack when that branch is used.
    /// </summary>
    public static void EmitNullJump(Label nullJump, Type type, Generator generator) {
        Label endOfLogic = generator.DefineLabel();
        generator.Emit(OpCodes.Br_S, endOfLogic);
        generator.MarkLabel(nullJump);
        EmitDefaultValue(type, generator);
        generator.MarkLabel(endOfLogic);
    }
    /// <summary>
    /// Writes the call, construction, or assignment for the supplied member.
    /// </summary>
    public static void EmitMemberDispatch(Generator generator, MemberInfo member) {
        if (member is ConstructorInfo ctor) {
            generator.Emit(OpCodes.Newobj, ctor);
            return;
        }
        if (member is FieldInfo f) {
            generator.Emit(OpCodes.Stfld, f);
            return;
        }
        if (member is PropertyInfo p) {
            var setter = p.GetSetMethod(nonPublic: true)
                ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Property {p.Name} has no setter");
            member = setter;
        }
        if (member is not MethodInfo m || m.DeclaringType is null)
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Member type {member.MemberType} is not supported for dispatch");
        if (m.IsVirtual && !m.IsStatic && !m.DeclaringType.IsValueType)
            generator.Emit(OpCodes.Callvirt, m);
        else
            generator.Emit(OpCodes.Call, m);
    }
}
/// <summary>Marks a plan that can read its value from the current row.</summary>
public interface ISimpleDbItemPlan {
    /// <summary>Writes the row read and leaves the value on the evaluation stack.</summary>
    void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint);
}

/// <summary>Exposes the single database column read by a plan.</summary>
public interface IColumnOrdinalPlan {
    /// <summary>The zero-based result-column ordinal.</summary>
    int ColumnOrdinal { get; }
}

/// <summary>Describes a composite plan that constructs a value from parameter and member plans.</summary>
public interface ICompositeDbItemPlan {
    /// <summary>The type constructed by this plan.</summary>
    Type ResultType { get; }
    /// <summary>The constructor or factory method used by this plan.</summary>
    MethodBase Construction { get; }
    /// <summary>The plans for constructor or factory parameters.</summary>
    IReadOnlyList<DbItemPlan> ConstructorArguments { get; }
    /// <summary>The plans for values assigned after construction.</summary>
    IReadOnlyList<(MemberInfo Member, DbItemPlan Plan)> PostMembers { get; }
    /// <summary>The grouping rules to try in priority order before inferred grouping.</summary>
    IReadOnlyList<IGroupingRule> GroupingRules { get; }
    /// <summary>The name matching settings used by this plan.</summary>
    ColModifier Context { get; }
}

/// <summary>
/// Base plan for a value that can be read from the current row. Derive from this type when a custom plan can
/// provide its own read instructions.
/// </summary>
public abstract class SimpleDbItemParser : DbItemPlan, ISimpleDbItemPlan {
    /// <summary>
    /// Writes the instructions that read this value from the current row.
    /// </summary>
    /// <param name="cols">The columns the result carries.</param>
    /// <param name="generator">The instruction writer to use.</param>
    /// <param name="nullSetPoint">Where to jump when a value is <c>NULL</c> and the object must collapse.</param>
    public abstract void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint);
}

/// <summary>
/// Base plan for a value read from one column.
/// Derive from this type to define how a database value is read after the standard null and order checks.
/// </summary>
public abstract class ScalarDbItemPlan(Type parentType, Type outputType, string parameterName,
    INullColHandler nullHandler, int ordinal) : SimpleDbItemParser, IColumnOrdinalPlan {
    /// <summary>The type containing the value being assigned.</summary>
    protected Type ParentType { get; } = parentType;
    /// <summary>The type left on the evaluation stack by <see cref="EmitValue"/>.</summary>
    protected Type OutputType { get; } = outputType;
    /// <summary>The mapped parameter or member name used by null errors.</summary>
    protected string ParameterName { get; } = parameterName;
    /// <summary>The target's null policy.</summary>
    protected INullColHandler NullHandler { get; } = nullHandler;
    /// <inheritdoc/>
    public int ColumnOrdinal { get; } = ordinal;

    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols)
        => cols[ColumnOrdinal].IsNullable && NullHandler.NeedNullJumpSetPoint(OutputType);

    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        if (previousIndex >= ColumnOrdinal)
            return false;
        previousIndex = ColumnOrdinal;
        return true;
    }

    /// <inheritdoc/>
    public sealed override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        var column = cols[ColumnOrdinal];
        if (!column.IsNullable) {
            EmitValue(column, generator);
            return;
        }
        Label notNull = generator.DefineLabel();
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, ColumnOrdinal);
        generator.Emit(OpCodes.Callvirt, TypeExtensions.IsNull);
        var branch = NullHandler.IsBr_S(OutputType) && nullSetPoint.NbOfPopToMake + 5 <= 127
            ? OpCodes.Brfalse_S : OpCodes.Brfalse;
        generator.Emit(branch, notNull);
        Label? endLabel = NullHandler.HandleNull(ParentType, OutputType, ParameterName, generator, nullSetPoint);
        generator.MarkLabel(notNull);
        EmitValue(column, generator);
        if (endLabel.HasValue)
            generator.MarkLabel(endLabel.Value);
    }

    /// <summary>
    /// Writes the non null read for <paramref name="column"/> and leaves exactly one
    /// <see cref="OutputType"/> value on the evaluation stack.
    /// </summary>
    /// <param name="column">The result column to read.</param>
    /// <param name="generator">The instruction writer to use.</param>
    protected abstract void EmitValue(ColumnInfo column, Generator generator);
}

/// <summary>A typed <see cref="ScalarDbItemPlan"/> that reads <typeparamref name="T"/>.</summary>
public abstract class ScalarDbItemPlan<T>(Type parentType, string parameterName, INullColHandler nullHandler,
    int ordinal) : ScalarDbItemPlan(parentType, typeof(T), parameterName, nullHandler, ordinal);
/// <summary>Marks where a custom read plan continues after a database <c>NULL</c> collapses a value.</summary>
public readonly struct NullSetPoint(Label Label, int NbOnStack) {
    /// <summary>
    /// Gets whether a valid recovery location is available.
    /// </summary>
    public readonly bool HasValue = true;
    private readonly Label Label = Label;
    private readonly int NbOnStack = NbOnStack;
    private NullSetPoint(Label Label, int NbOnStack, bool HasValue) : this(Label, NbOnStack) {
        this.HasValue = HasValue;
    }
    /// <summary>
    /// Gets the number of evaluation stack items removed when jumping.
    /// </summary>
    public readonly int NbOfPopToMake => NbOnStack;
    /// <summary>
    /// Returns a new value that includes added evaluation stack items.
    /// </summary>
    public NullSetPoint WithItemOnStack(int nbItemOnStack)
        => new(Label, NbOnStack + nbItemOnStack, HasValue);
    /// <summary>
    /// Removes tracked evaluation stack items and jumps to the recovery location.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no recovery label is defined.</exception>
    public void MakeNullJump(Generator generator) {
        if (!HasValue)
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, "a null jump was made without a label to jump to");
        for (int i = 0; i < NbOnStack; i++)
            generator.Emit(OpCodes.Pop);
        generator.Emit(OpCodes.Br, Label);
    }
}

/// <summary>
/// Describes a plan that adds one element from each row to an accumulated result.
/// Implement this interface for a custom multi row plan.
/// </summary>
public interface IMultiRowPlan {
    /// <summary>The plan that reads one element from a row.</summary>
    DbItemPlan Element { get; }
    /// <summary>The type supplied to the accumulator's add method.</summary>
    Type ElementType { get; }
    /// <summary>The type stored while rows are being folded.</summary>
    Type BufferType { get; }
    /// <summary>The method that folds one element into the accumulator.</summary>
    System.Reflection.MethodInfo AddMethod { get; }
    /// <summary>The construction that creates an empty accumulator.</summary>
    System.Reflection.MethodBase InitialState { get; }
    /// <summary>The construction that turns the accumulator into the result, or <see langword="null"/> when it is the result.</summary>
    System.Reflection.MethodBase? Construct { get; }
    /// <summary>The null rule for an element that collapses during reading.</summary>
    INullColHandler NullRule { get; }
}
