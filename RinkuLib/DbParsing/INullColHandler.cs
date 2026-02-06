using System.Reflection.Emit;

namespace RinkuLib.DbParsing;

/// <summary>
/// Specifies that an instantiation members need to jump if DBNull.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class InvalidOnNullAttribute : Attribute;
/// <summary>
/// Defines a factory for creating <see cref="INullColHandler"/> instances based on reflection metadata.
/// </summary>
public interface INullColHandlerMaker {
    /// <summary>
    /// Creates a null handler for a specific member or parameter.
    /// </summary>
    public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param);
}
/// <summary>
/// Defines how the IL Generator should react when a database column contains a NULL value.
/// </summary>
public interface INullColHandler {
    /// <summary>
    /// Indicates if this handler requires a <see cref="NullSetPoint"/> (a jump target) to be defined in the IL stream.
    /// </summary>
    public bool NeedNullJumpSetPoint(Type closedType);
    /// <summary>
    /// Determines if the branch emitted by this handler uses a short-form instruction (Br_S).
    /// </summary>
    public bool IsBr_S(Type closedType);
    /// <summary>
    /// Emits the IL instructions to handle a null value.
    /// </summary>
    /// <returns>A label to branch to after handling, or null if the handler performs an absolute jump/throw.</returns>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint);
    /// <summary>
    /// Returns a handler configured with the specified jump behavior.
    /// </summary>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull);
}
public class NullableTypeHandle : INullColHandler {
    public static readonly NullableTypeHandle Instance = new();
    private NullableTypeHandle() { }
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        var endLabel = generator.DefineLabel();
        DbItemParser.EmitDefaultValue(closedType, generator);
        generator.Emit(OpCodes.Br_S, endLabel);
        return endLabel;
    }
    public bool IsBr_S(Type closedType) => true;
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) 
        => invalidOnNull ? NullJumpAndNullableHandle.Instance : this;
}
public class NullJumpAndNullableHandle : INullColHandler {
    public static NullJumpAndNullableHandle Instance { get; } = new();
    private NullJumpAndNullableHandle() { }
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    public bool IsBr_S(Type closedType) => true;
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? this : NullableTypeHandle.Instance;
}
public class NotNullHandle : INullColHandler {
    public static readonly NotNullHandle Instance = new();
    private NotNullHandle() {}
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        DbItemParser.EmitThrowNullAssignment(closedType, paramName, generator);
        return null;
    }
    public bool IsBr_S(Type closedType) => true;
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? NullJumpAndNotNullHandle.Instance : this;
}
public class NullJumpAndNotNullHandle : INullColHandler {
    public static NullJumpAndNotNullHandle Instance { get; } = new();
    private NullJumpAndNotNullHandle() { }
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    public bool IsBr_S(Type closedType) => true;
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? this : NotNullHandle.Instance;
}