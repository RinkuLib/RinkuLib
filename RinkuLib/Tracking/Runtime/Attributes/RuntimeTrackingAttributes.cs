using System;

namespace Rinku.Tracking.Runtime;

/// <summary>Marks a generated member as runtime-only.</summary>
public sealed class RuntimeValueAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Applies the runtime-only setting.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.RuntimeValue();
}

/// <summary>Excludes a member from generated tracking.</summary>
public sealed class TrackingIgnoreAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Applies the ignore setting.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.Ignore = true;
}

/// <summary>Makes a generated member read-only.</summary>
public sealed class TrackingReadOnlyAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Applies the read-only setting.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.ReadOnly();
}

/// <summary>Makes a generated member writable.</summary>
public sealed class TrackingWritableAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Applies the writable setting.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.Writable();
}


/// <summary>Binds a generated member to an original member for reading and writing.</summary>
public sealed class BindToAttribute(string memberName) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets the original member name.</summary>
    public string MemberName { get; } = memberName;
    /// <summary>Applies the binding.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) {
        member.ReadFrom(MemberName);
        member.WriteTo(MemberName);
    }
}

/// <summary>Binds generated reads to an original member.</summary>
public sealed class ReadFromAttribute(string memberName) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets the original member name.</summary>
    public string MemberName { get; } = memberName;
    /// <summary>Applies the read binding.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.ReadFrom(MemberName);
}

/// <summary>Binds generated writes to an original member.</summary>
public sealed class WriteToAttribute(string memberName) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets the original member name.</summary>
    public string MemberName { get; } = memberName;
    /// <summary>Applies the write binding.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.WriteTo(MemberName);
}

/// <summary>Binds generated reads to a method.</summary>
public sealed class ReadWithAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Creates a method-based read binding.</summary>
    public ReadWithAttribute(string methodName) { MethodName = methodName; }
    /// <summary>Creates a type-qualified method-based read binding.</summary>
    public ReadWithAttribute(Type type, string methodName) { Type = type; MethodName = methodName; }
    /// <summary>Gets the source type.</summary>
    public Type? Type { get; }
    /// <summary>Gets the source method name.</summary>
    public string MethodName { get; }
    /// <summary>Applies the read binding.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) {
        if (Type is null) member.ReadWith(MethodName);
        else member.ReadWith(Type, MethodName);
    }
}

/// <summary>Binds generated writes to a method.</summary>
public sealed class WriteWithAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Creates a method-based write binding.</summary>
    public WriteWithAttribute(string methodName) { MethodName = methodName; }
    /// <summary>Creates a type-qualified method-based write binding.</summary>
    public WriteWithAttribute(Type type, string methodName) { Type = type; MethodName = methodName; }
    /// <summary>Gets the source type.</summary>
    public Type? Type { get; }
    /// <summary>Gets the source method name.</summary>
    public string MethodName { get; }
    /// <summary>Applies the write binding.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) {
        if (Type is null) member.WriteWith(MethodName);
        else member.WriteWith(Type, MethodName);
    }
}

/// <summary>Excludes a member from named and indexed runtime access.</summary>
public sealed class NoRuntimeAccessAttribute : RuntimeTrackingMemberAttribute {
    /// <summary>Applies the runtime-access exclusion.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.IncludeInRuntimeAccess = false;
}

// Controls direct and UseWith parameter projection independently from CLR/runtime access.
/// <summary>Controls parameter projection for a generated member.</summary>
public sealed class RuntimeParameterAttribute(bool enabled = true) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets whether parameter projection is enabled.</summary>
    public bool Enabled { get; } = enabled;
    /// <summary>Applies parameter projection.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.Parameter(Enabled);
}

/// <summary>Sets the parameter projection name for a generated member.</summary>
[Obsolete("Use Rinku.Querying.Parameters.ParameterNameAttribute instead.")]
public sealed class RuntimeParameterNameAttribute(string name) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets the parameter name.</summary>
    public string Name { get; } = name;
    /// <summary>Applies the parameter name.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.ParameterName(Name);
}

/// <summary>Adds an alternate parameter projection name.</summary>
[Obsolete("Use Rinku.Querying.Parameters.ParameterAliasAttribute instead.")]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public sealed class RuntimeParameterAliasAttribute(string name) : RuntimeTrackingMemberAttribute {
    /// <summary>Gets the alias name.</summary>
    public string Name { get; } = name;
    /// <summary>Applies the parameter alias.</summary>
    public override void Apply(RuntimeTrackingMemberOptions member) => member.ParameterAlias(Name);
}
