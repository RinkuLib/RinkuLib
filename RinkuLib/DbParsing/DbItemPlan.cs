using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Exceptions;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>Identify a parameter that should not be null, but the row provided a null value. Can be direcly a null column or a sub-class that lead to a null value</summary>
public class NullValueAssignmentException(Type parentType, Type paramType, string paramName) : RinkuReadException(ErrorCodes.NullNotAllowed, $"Constraint Violation: Parameter '{paramName}' of type '{paramType.Name}' is marked as non-nullable, but the source provided a null value. Parent: {parentType}") {
    /// <summary>The name of the parameter that should not be null</summary>
    public readonly string ParameterName = paramName;
    /// <summary>The type of the parent</summary>
    public readonly Type ParentType = parentType;
    /// <summary>The type of the parameter that should not be null</summary>
    public readonly Type ParameterType = paramType;
}
/// <summary>
/// One node in a type's read plan, it knows how to read its own part of a row, whether a single column, a
/// nested object, or a default. The plan is a tree of these, and the object parser is compiled from it.
/// </summary>
public abstract class DbItemPlan {
    /// <summary>
    /// Whether reading this node can hit a <c>NULL</c> that has to collapse the owning object, so a recovery
    /// point is needed.
    /// </summary>
    public abstract bool NeedNullSetPoint(ColumnInfo[] cols);
    /// <summary>
    /// Whether this node reads its columns in order, the condition that lets the result stream sequentially.
    /// </summary>
    public abstract bool IsSequencial(ref int previousIndex);
    /// <summary>
    /// This node's children in the plan tree, for the walk that decides whether the whole plan is single-row.
    /// Leaves have none.
    /// </summary>
    public virtual IEnumerable<DbItemPlan> Children => [];
    /// <summary>
    /// Whether <paramref name="node"/> and everything beneath it is a <see cref="SimpleDbItemParser"/>, the
    /// one condition for the single compiled delegate. There is no capability flag, the ability to emit single
    /// row is simply being the specialized type, checked here recursively. A collection or other node that is
    /// not one fails it, so any plan containing one takes the multi-row road.
    /// </summary>
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
    /// Emits a throw instruction for an <see cref="NullValueAssignmentException"/> when 
    /// a non-nullable target receives a null value from the schema.
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
    /// Emits the instructions to place the default value of a <see cref="Type"/> onto the stack.
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
    /// Generates the branching logic for null recovery, marking the jump label and 
    /// loading the default value if the jump is triggered.
    /// </summary>
    public static void EmitNullJump(Label nullJump, Type type, Generator generator) {
        Label endOfLogic = generator.DefineLabel();
        generator.Emit(OpCodes.Br_S, endOfLogic);
        generator.MarkLabel(nullJump);
        EmitDefaultValue(type, generator);
        generator.MarkLabel(endOfLogic);
    }
    /// <summary>
    /// Dispatches the appropriate IL instruction (Call, Callvirt, Stfld, or Newobj) 
    /// based on whether the member is a Method, Property, Field, or Constructor.
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
/// <summary>Emits a plan that reads its value from the current row.</summary>
public interface ISimpleDbItemPlan {
    /// <summary>Emits the row read and leaves the value on the evaluation stack.</summary>
    void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject);
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
    /// <summary>The explicit grouping rule, or null when the emitter should infer one.</summary>
    IGroupingRule? GroupKey { get; }
    /// <summary>The name matching context used while negotiating this plan.</summary>
    ColModifier Context { get; }
}

/// <summary>
/// A node that can read its value from the current row alone, the more specific parser the single-row road is
/// built from. The base <see cref="DbItemPlan"/> is the general, multi-row case and carries no single-row
/// emit, this is the specialization that does. When every node in a plan is one of these the plan takes the
/// happy path, the single compiled delegate, otherwise the multi-row road negotiates a state object instead.
/// </summary>
public abstract class SimpleDbItemParser : DbItemPlan, ISimpleDbItemPlan {
    /// <summary>
    /// Reads this node's value from the current row, handling a <c>NULL</c> and any type conversion.
    /// </summary>
    /// <param name="cols">The columns the result carries.</param>
    /// <param name="generator">The IL generator the read is written into.</param>
    /// <param name="nullSetPoint">Where to jump when a value is <c>NULL</c> and the object must collapse.</param>
    /// <param name="targetObject">Set to an object the compiled reader needs passed in, or <see langword="null"/>.</param>
    public abstract void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject);
}
/// <summary>
/// A recovery location (Jump Point) used during IL emission to handle null values.
/// </summary>
public readonly struct NullSetPoint(Label Label, int NbOnStack) {
    /// <summary>
    /// Indicates if a valid recovery label is defined for the current context.
    /// </summary>
    public readonly bool HasValue = true;
    private readonly Label Label = Label;
    private readonly int NbOnStack = NbOnStack;
    private NullSetPoint(Label Label, int NbOnStack, bool HasValue) : this(Label, NbOnStack) {
        this.HasValue = HasValue;
    }
    /// <summary>
    /// The number of items currently on the evaluation stack that must be
    /// removed if a null jump occurs.
    /// </summary>
    public readonly int NbOfPopToMake => NbOnStack;
    /// <summary>
    /// Returns a new context tracking additional items placed on the stack.
    /// </summary>
    public NullSetPoint WithItemOnStack(int nbItemOnStack)
        => new(Label, NbOnStack + nbItemOnStack, HasValue);
    /// <summary>
    /// Emits the necessary <c>Pop</c> instructions to clear the stack and 
    /// branches to the recovery label.
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
/// Describes a plan that folds one element from each row into an accumulator.
/// The default multi-row emitter consumes this capability; another plan implementation can provide the same
/// behavior without deriving from the built-in accumulator plan.
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
