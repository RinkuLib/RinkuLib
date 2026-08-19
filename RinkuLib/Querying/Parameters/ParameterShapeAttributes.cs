namespace Rinku.Querying.Parameters;

/// <summary>Controls what happens when two equally valid sources provide the same parameter.</summary>
public enum ParameterConflictBehavior : byte {
    /// <summary>Throws while the cached parameter accessor is being created.</summary>
    Throw,
    /// <summary>Accepts one equally ranked source. Which source wins is intentionally unspecified.</summary>
    TakeOne
}

/// <summary>Configures equal-priority parameter-source conflicts for a parameter object type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true)]
public sealed class ParameterConflictAttribute(ParameterConflictBehavior behavior) : Attribute {
    /// <summary>The conflict behavior used after normal source priority has been applied.</summary>
    public ParameterConflictBehavior Behavior { get; } = behavior;
}

/// <summary>Uses a different parameter/condition name for a member.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
public sealed class ParameterNameAttribute(string name) : Attribute {
    /// <summary>The parameter name, with or without the command's variable prefix.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A parameter name cannot be empty.", nameof(name))
        : name;
}

/// <summary>Adds another parameter/condition name for a member.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class ParameterAliasAttribute(string name) : Attribute {
    /// <summary>The additional parameter name, with or without the command's variable prefix.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A parameter alias cannot be empty.", nameof(name))
        : name;
}

/// <summary>Prevents a public member from participating in parameter binding.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
public sealed class ParameterIgnoreAttribute : Attribute;

/// <summary>
/// Flattens a member's parameter surface into its containing object. The nested object's members behave as
/// though they were declared on the containing parameter object.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
public sealed class NestedParametersAttribute : Attribute {
    /// <summary>Flattens the nested object without a prefix.</summary>
    public NestedParametersAttribute() { }

    /// <summary>
    /// Flattens the nested object and prefixes every nested parameter name. The prefix is concatenated
    /// directly, so pass <c>"Employee"</c> for <c>EmployeeId</c> or <c>"Employee_"</c> for <c>Employee_Id</c>.
    /// </summary>
    public NestedParametersAttribute(string prefix)
        => Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));

    /// <summary>The optional prefix prepended to nested parameter names.</summary>
    public string? Prefix { get; }
}
