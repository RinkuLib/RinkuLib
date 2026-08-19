using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rinku.Tracking.Runtime;

// Type-wide generation attributes are intentionally separate from member attributes. They run before
// strong-contract properties are applied, so they define the model that individual members can refine.
/// <summary>Configures a generated tracking type.</summary>
public interface IRuntimeTrackingTypeAttribute {
    /// <summary>Gets the application order.</summary>
    int Order { get; }
    /// <summary>Applies the type configuration.</summary>
    void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type);
}

/// <summary>Base class for generated tracking type attributes.</summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public abstract class RuntimeTrackingTypeAttribute : Attribute, IRuntimeTrackingTypeAttribute {
    /// <summary>Gets or sets the application order.</summary>
    public int Order { get; set; }
    /// <summary>Applies the type policy.</summary>
    public abstract void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type);
}

/// <summary>Provides context while configuring a generated tracking type.</summary>
public sealed class RuntimeTrackingTypeContext<TOriginal> {
    internal RuntimeTrackingTypeContext(RuntimeTrackingOptions<TOriginal> options, Type exposedContract, Type sourceType) {
        Options = options;
        ExposedContract = exposedContract;
        SourceType = sourceType;
    }

    /// <summary>Gets the runtime options.</summary>
    public RuntimeTrackingOptions<TOriginal> Options { get; }
    /// <summary>Gets the original type.</summary>
    public Type OriginalType => typeof(TOriginal);
    /// <summary>Gets the exposed contract.</summary>
    public Type ExposedContract { get; }
    /// <summary>Gets the source type.</summary>
    public Type SourceType { get; }
    /// <summary>Gets whether the source is the original type.</summary>
    public bool IsOriginalType => SourceType == typeof(TOriginal);
}

// Called once for every strong-contract property group after the contract signature/read-only default
// has been established, but before member attributes. Replacing this convention changes how the whole
// contract is interpreted while still letting the closest member declaration override it locally.
/// <summary>Configures one generated contract member.</summary>
public delegate void RuntimeTrackingContractMemberConvention<TOriginal>(RuntimeTrackingContractMemberContext<TOriginal> member);

/// <summary>Provides context while configuring a generated contract member.</summary>
public sealed class RuntimeTrackingContractMemberContext<TOriginal> {
    internal RuntimeTrackingContractMemberContext(RuntimeTrackingOptions<TOriginal> options, Type exposedContract, RuntimeTrackingMemberOptions member, IReadOnlyList<PropertyInfo> declarations, bool requiresGetter, bool requiresSetter) {
        Options = options;
        ExposedContract = exposedContract;
        Member = member;
        Declarations = declarations;
        RequiresGetter = requiresGetter;
        RequiresSetter = requiresSetter;
    }

    /// <summary>Gets the runtime options.</summary>
    public RuntimeTrackingOptions<TOriginal> Options { get; }
    /// <summary>Gets the original type.</summary>
    public Type OriginalType => typeof(TOriginal);
    /// <summary>Gets the exposed contract.</summary>
    public Type ExposedContract { get; }
    /// <summary>Gets the member options.</summary>
    public RuntimeTrackingMemberOptions Member { get; }
    /// <summary>Gets the contract declarations.</summary>
    public IReadOnlyList<PropertyInfo> Declarations { get; }
    /// <summary>Gets whether a getter is required.</summary>
    public bool RequiresGetter { get; }
    /// <summary>Gets whether a setter is required.</summary>
    public bool RequiresSetter { get; }
}

// Useful built-ins. They are ordinary type attributes using the same extensibility point as user-defined policies.
// Extra original members default to non-public CLR accessors so the strong interface remains the compile-time surface;
// IRuntimeMemberAccess can still expose them by name/index.
/// <summary>Includes original members in a generated type.</summary>
public sealed class IncludeOriginalMembersAttribute : RuntimeTrackingTypeAttribute {
    /// <summary>Gets or sets whether generated properties are exposed.</summary>
    public bool ExposeProperties { get; set; }
    /// <summary>Gets or sets whether generated members support runtime access.</summary>
    public bool IncludeInRuntimeAccess { get; set; } = true;
    /// <summary>Applies the original-member policy.</summary>
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type)
        => type.Options.IncludeOriginalMembers(ExposeProperties, IncludeInRuntimeAccess);
}

/// <summary>Controls generated runtime member access.</summary>
public sealed class RuntimeDynamicAccessAttribute(bool enabled = true) : RuntimeTrackingTypeAttribute {
    /// <summary>Gets whether runtime access is enabled.</summary>
    public bool Enabled { get; } = enabled;
    /// <summary>Applies the runtime-access policy.</summary>
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type) => type.Options.DynamicAccess(Enabled);
}

/// <summary>Controls generated property notifications.</summary>
public sealed class RuntimeNotificationsAttribute(bool enabled = true) : RuntimeTrackingTypeAttribute {
    /// <summary>Gets whether notifications are enabled.</summary>
    public bool Enabled { get; } = enabled;
    /// <summary>Applies the notification policy.</summary>
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type) => type.Options.Notifications(Enabled);
}

// Whole-type default for direct and UseWith parameter projection. Member-level
// [RuntimeParameter] / name / alias attributes can still override the generated members afterwards.
/// <summary>Controls generated parameter projection.</summary>
public sealed class RuntimeParametersAttribute(bool enabled = true) : RuntimeTrackingTypeAttribute {
    /// <summary>Gets whether parameter projection is enabled.</summary>
    public bool Enabled { get; } = enabled;
    /// <summary>Applies the parameter policy.</summary>
    public override void Apply<TOriginal>(RuntimeTrackingTypeContext<TOriginal> type) => type.Options.Parameters(Enabled);
}
