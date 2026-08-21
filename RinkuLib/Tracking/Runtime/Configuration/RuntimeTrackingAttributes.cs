namespace Rinku.Tracking.Runtime;

/// <summary>Configuration attributes mutate the first-phase member tree and are not copied to the generated CLR property.</summary>
public interface IRuntimeTrackingConfigurationAttribute
{
    /// <summary>Applies configuration to a member.</summary>
    void Apply(RuntimeTrackingMemberConfigurator member);
}

/// <summary>Marks a generated member as read-only.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class RuntimeReadOnlyAttribute : Attribute, IRuntimeTrackingConfigurationAttribute
{
    /// <inheritdoc/>
    public void Apply(RuntimeTrackingMemberConfigurator member) => member.ReadOnly();
}

/// <summary>Enables editing for a nested member.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class NestedEditAttribute : Attribute, IRuntimeTrackingConfigurationAttribute
{
    public NestedEditAttribute() { }
    /// <summary>Creates the attribute with a nested edit mode.</summary>
    public NestedEditAttribute(NestedEditMode mode) => Mode = mode;
    /// <summary>Gets the nested edit mode.</summary>
    public NestedEditMode Mode { get; } = NestedEditMode.InPlace;
    /// <inheritdoc/>
    public void Apply(RuntimeTrackingMemberConfigurator member) => member.NestedEdit(Mode);
}

/// <summary>Removes a member from runtime generation.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class RuntimeIgnoreAttribute : Attribute, IRuntimeTrackingConfigurationAttribute
{
    /// <inheritdoc/>
    public void Apply(RuntimeTrackingMemberConfigurator member) => member.Ignore();
}

/// <summary>Explicitly stores a runtime-only member directly on TEdit, outside the edit snapshot.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class RuntimeDirectAttribute : Attribute, IRuntimeTrackingConfigurationAttribute
{
    /// <inheritdoc/>
    public void Apply(RuntimeTrackingMemberConfigurator member) => member.Direct();
}

/// <summary>Explicitly gives a runtime-only member accepted TEdit storage plus lazy snapshot participation.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class RuntimeSnapshotValueAttribute : Attribute, IRuntimeTrackingConfigurationAttribute
{
    /// <inheritdoc/>
    public void Apply(RuntimeTrackingMemberConfigurator member) => member.SnapshotValue();
}
