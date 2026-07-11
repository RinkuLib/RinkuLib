using System.Reflection.Emit;

namespace RinkuLib.DbParsing;
/// <summary>The seam behind the reading-order attributes, it adjusts how a member claims its columns.</summary>
public interface IUsageFlagModifier {
    /// <summary>Adjusts the reading-order flags for the member this is on.</summary>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag);
}
/// <summary>
/// Collapses the owning object to nothing when this column is <c>NULL</c>, so a nested object that is all
/// nulls becomes absent instead of an instance of blanks.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InvalidOnNullAttribute : Attribute;
/// <summary>
/// The member may look anywhere in the schema to find its column, not only the one following the last
/// consumed. On a complex-typed slot this frees only the subtree's first consumed column. The rest keep
/// the inherited regime. Use <see cref="CanLookAnywhereSubtreeAttribute"/> to free the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanLookAnywhereAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.RemoveSequentialRead;
}
/// <summary>
/// The member must <b>not</b> look anywhere and must take only the column following the last consumed.
/// On a complex-typed slot this constrains only the subtree's first consumed column. The rest keep the
/// inherited regime. Use <see cref="CanNotLookAnywhereSubtreeAttribute"/> to constrain the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanNotLookAnywhereAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.SequentialRead;
}
/// <summary>
/// Specifies that an an allready used column may be used to match. On a complex-typed slot this applies
/// to the subtree's first consumed column. Use <see cref="MayReuseColSubtreeAttribute"/> for the whole subtree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MayReuseColAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.CanReuse;
}
/// <summary>
/// The subtree form of <see cref="CanLookAnywhereAttribute"/>: frees the complex slot's whole subtree to
/// look anywhere, not just its first column.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanLookAnywhereSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.RemoveSequentialRead | UsageFlags.Subtree;
}
/// <summary>
/// The subtree form of <see cref="CanNotLookAnywhereAttribute"/>: constrains the complex slot's whole
/// subtree to sequential reading, not just its first column.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CanNotLookAnywhereSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.SequentialRead | UsageFlags.Subtree;
}
/// <summary>
/// The subtree form of <see cref="MayReuseColAttribute"/>: lets the complex slot's whole subtree reuse
/// already consumed columns, not just its first column.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MayReuseColSubtreeAttribute : Attribute, IUsageFlagModifier {
    /// <inheritdoc/>
    public void UpdateFlags(object? param, ref UsageFlags usageFlag)
        => usageFlag |= UsageFlags.CanReuse | UsageFlags.Subtree;
}
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
    /// The same rule switched to also collapse the owning object when the value is <c>NULL</c>, or back.
    /// </summary>
    public INullColHandler SetInvalidOnNull(Type type, bool invalidOnNull);
}
/// <summary>The null rule that substitutes the type's default when a column is <c>NULL</c>.</summary>
public class NullableTypeHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NullableTypeHandle Instance = new();
    private NullableTypeHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
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
/// <summary>The null rule that collapses the owning object when a column is <c>NULL</c>, otherwise a default.</summary>
public class InvalidOnNullAndNullableHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static InvalidOnNullAndNullableHandle Instance { get; } = new();
    private InvalidOnNullAndNullableHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
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
/// <summary>The null rule that throws when a column is <c>NULL</c>, the default for a non-nullable value.</summary>
public class NotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static readonly NotNullHandle Instance = new();
    private NotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
        DbItemParser.EmitThrowNullAssignment(parentType, closedType, paramName, generator);
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
/// <summary>The null rule that collapses the owning object when a column is <c>NULL</c>, otherwise throws.</summary>
public class InvalidOnNullAndNotNullHandle : INullColHandler {
    /// <summary>Singleton</summary>
    public static InvalidOnNullAndNotNullHandle Instance { get; } = new();
    private InvalidOnNullAndNotNullHandle() { }
    /// <inheritdoc/>
    public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
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