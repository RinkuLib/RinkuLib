using System.Reflection.Emit;

namespace RinkuLib.DbParsing;
/// <summary>
/// Collapses the owning object to nothing when this column is <c>NULL</c>, so a nested object that is all
/// nulls becomes absent instead of an instance of blanks.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AbortOnNullAttribute : Attribute;
/// <summary>
/// Builds the null rule for a member from its reflection metadata, the seam behind an attribute that changes
/// how a column's <c>NULL</c> is treated.
/// </summary>
public interface INullColHandlerMaker {
    /// <summary>
    /// Builds the null rule for a member or parameter.
    /// </summary>
    public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param);
}
/// <summary>
/// What a <c>NULL</c> column means for a value, take a default, throw, or collapse the object it belongs to.
/// This is the column-level counterpart to the null-accepting result shapes.
/// </summary>
public interface INullColHandler {
    /// <summary>Whether handling this null needs a jump target set up beforehand, used internally while emitting.</summary>
    public bool NeedNullJumpSetPoint(Type closedType);
    /// <summary>Whether the branch this handler emits is short-form, an emit detail.</summary>
    public bool IsBr_S(Type closedType);
    /// <summary>
    /// Emits how a <c>NULL</c> is handled for this value.
    /// </summary>
    /// <returns>A label to continue at after handling, or <see langword="null"/> when the handler jumps or throws outright.</returns>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint);
    /// <summary>
    /// Emits how a <c>NULL</c> element signal is handled in a multi-row collection, storing the result in the element local.
    /// </summary>
    /// <returns>A label to continue at after handling, or <see langword="null"/> when the handler jumps or throws outright.</returns>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint);
    /// <summary>
    /// The same rule switched to also collapse the owning object when the value is <c>NULL</c>, or back.
    /// </summary>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull);
}
/// <summary>The null rule that substitutes the type's default when a column is <c>NULL</c>.</summary>
public class NullableTypeHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NullableTypeHandle Instance = new();
    private NullableTypeHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        var endLabel = generator.DefineLabel();
        DbItemPlan.EmitDefaultValue(closedType, generator);
        generator.Emit(OpCodes.Br_S, endLabel);
        return endLabel;
    }
    /// <inheritdoc/>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
        throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType,
            $"nullable type handling does not support multi-row null elements; use [KeepNullElements] for {bufferType}");
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    /// <inheritdoc/>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull)
        => abortOnNull ? AbortOnNullAndNullableHandle.Instance : this;
}
/// <summary>The null rule for collection elements marked with [KeepNullElements], keeps the element as the type's default.</summary>
public class KeepNullElementsHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly KeepNullElementsHandle Instance = new();
    private KeepNullElementsHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        throw new RinkuConfigurationException(ErrorCodes.OperationNotSupportedForType,
            $"[KeepNullElements] only supports collection elements, not simple types like {closedType}");
    }
    /// <inheritdoc/>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
        DbItemPlan.EmitDefaultValue(elementType, generator);
        generator.Emit(OpCodes.Stloc, elementLocal);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    /// <inheritdoc/>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull)
        => abortOnNull ? this : this;
}
/// <summary>The null rule that collapses the owning object when a column is <c>NULL</c>, otherwise a default.</summary>
public class AbortOnNullAndNullableHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static AbortOnNullAndNullableHandle Instance { get; } = new();
    private AbortOnNullAndNullableHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    /// <inheritdoc/>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull)
        => abortOnNull ? this : NullableTypeHandle.Instance;
}
/// <summary>The null rule that throws when a column is <c>NULL</c>, the default for a non-nullable value.</summary>
public class NotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NotNullHandle Instance = new();
    private NotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        DbItemPlan.EmitThrowNullAssignment(parentType, closedType, paramName, generator);
        return null;
    }
    /// <inheritdoc/>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
        DbItemPlan.EmitThrowNullAssignment(bufferType, elementType, paramName, generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => false;
    /// <inheritdoc/>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull)
        => abortOnNull ? AbortOnNullAndNotNullHandle.Instance : this;
}
/// <summary>The null rule that collapses the owning object when a column is <c>NULL</c>, otherwise throws.</summary>
public class AbortOnNullAndNotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static AbortOnNullAndNotNullHandle Instance { get; } = new();
    private AbortOnNullAndNotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
        nullSetPoint.MakeNullJump(generator);
        return null;
    }
    /// <inheritdoc/>
    public bool IsBr_S(Type closedType) => true;
    /// <inheritdoc/>
    public bool NeedNullJumpSetPoint(Type closedType) => true;
    /// <inheritdoc/>
    public INullColHandler SetAbortOnNull(Type type, bool abortOnNull)
        => abortOnNull ? this : NotNullHandle.Instance;
}