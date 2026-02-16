using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>Identify a parameter that should not be null, but the row provided a null value. Can be direcly a null column or a sub-class that lead to a null value</summary>
public class NullValueAssignmentException(Type paramType, string paramName) : Exception($"Constraint Violation: Parameter '{paramName}' of type '{paramType.Name}' is marked as non-nullable, but the source provided a null value.") {
    /// <summary>The name of the parameter that should not be null</summary>
    public readonly string ParameterName = paramName;
    /// <summary>The type of the parameter that should not be null</summary>
    public readonly Type ParameterType = paramType;
}
/// <summary>
/// The abstract base for all emission nodes. Defines how a specific part of the 
/// object graph generates its IL instructions.
/// </summary>
public abstract class DbItemParser {
    /// <summary>
    /// Determines if this node requires a recovery point to handle null values 
    /// based on the current schema.
    /// </summary>
    public abstract bool NeedNullSetPoint(ColumnInfo[] cols);
    /// <summary>
    /// Used for optimization to check if column access follows a sequential index order.
    /// </summary>
    public abstract bool IsSequencial(ref int previousIndex);
    /// /// <summary>
    /// The core emitter. Generates the IL to fetch data, handle DBNull, and convert types.
    /// </summary>
    /// <param name="cols">Metadata for all available columns.</param>
    /// <param name="generator">The IL generator wrapper.</param>
    /// <param name="nullSetPoint">The recovery point when value is null.</param>
    public abstract void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint);
    internal static readonly ConstructorInfo NullAssignmentCtor = typeof(NullValueAssignmentException).GetConstructor([typeof(Type), typeof(string)])!;
    internal static readonly MethodInfo GetTypeHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!;

    /// <summary>
    /// Emits a throw instruction for an <see cref="NullValueAssignmentException"/> when 
    /// a non-nullable target receives a null value from the schema.
    /// </summary>
    public static void EmitThrowNullAssignment(Type paramType, string paramName, Generator generator) {
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
                ?? throw new InvalidOperationException($"Property {p.Name} has no setter.");
            member = setter;
        }
        if (member is not MethodInfo m || m.DeclaringType is null)
            throw new NotSupportedException($"Member type {member.MemberType} is not supported for dispatch.");
        if (m.IsVirtual && !m.IsStatic && !m.DeclaringType.IsValueType) 
            generator.Emit(OpCodes.Callvirt, m);
        else 
            generator.Emit(OpCodes.Call, m);
    }
}
/// <summary>
/// Represents a recovery location (Jump Point) used during IL emission to handle null values.
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
            throw new InvalidOperationException("must have a label defined");
        for (int i = 0; i < NbOnStack; i++)
            generator.Emit(OpCodes.Pop);
        generator.Emit(OpCodes.Br, Label);
    }
}