using System.Reflection.Emit;

namespace RinkuLib.DbParsing;

/// <summary>
/// Specifies that an instantiation members need to jump if DBNull.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class InvalidOnNullAttribute : Attribute;
/// <summary>
/// Specifies that an the member may look anywhere in the schema to find matching col not only the column folowing the one previously used.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CanLookAnywhereAttribute : Attribute;
/// <summary>
/// Specifies that an the member may <b>not</b> look anywhere in the schema to find matching col and must only use the one folowing the one previously used.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CanNotLookAnywhereAttribute : Attribute;
/// <summary>
/// Specifies that an an allready used column may be used to match
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MayReuseColAttribute : Attribute;
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
/// <summary>Emit a default value when null</summary>
public class NullableTypeHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NullableTypeHandle Instance = new();
    private NullableTypeHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        var endLabel = generator.DefineLabel();
        DbItemParser.EmitDefaultValue(closedType, generator);
        generator.Emit(OpCodes.Br_S, endLabel);
        return endLabel;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    /// <inheritdoc/>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull) 
        => invalidOnNull ? InvalidOnNullAndNullableHandle.Instance : this;
}
/// <summary>Jump to the previous setpoint when null</summary>
public class InvalidOnNullAndNullableHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static InvalidOnNullAndNullableHandle Instance { get; } = new();
    private InvalidOnNullAndNullableHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    /// <inheritdoc/>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? this : NullableTypeHandle.Instance;
}
/// <summary>Emit a throw exception when null</summary>
public class NotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NotNullHandle Instance = new();
    private NotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        DbItemParser.EmitThrowNullAssignment(closedType, paramName, generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    /// <inheritdoc/>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? InvalidOnNullAndNotNullHandle.Instance : this;
}
/// <summary>Jump to the previous setpoint when null</summary>
public class InvalidOnNullAndNotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static InvalidOnNullAndNotNullHandle Instance { get; } = new();
    private InvalidOnNullAndNotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    /// <inheritdoc/>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull)
        => invalidOnNull ? this : NotNullHandle.Instance;
}