namespace Rinku.Tracking.Committers;

/// <summary>Names a typed output's destination in the processing parameter bag and its source on the command factory.</summary>
public abstract class CommandOutput {
    private protected CommandOutput(string name, string? sourceName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sourceName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        Name = name;
        SourceName = sourceName;
    }
    /// <summary>Gets the destination name for the pending value.</summary>
    public string Name { get; }
    /// <summary>Gets the command output name, when it differs from the destination name.</summary>
    public string? SourceName { get; }
    /// <summary>Gets the type of the command output.</summary>
    public abstract Type ValueType { get; }
    /// <summary>Creates a typed command output mapping.</summary>
    public static CommandOutput<T> Named<T>(string name, string? sourceName = null) => new(name, sourceName);
}

/// <summary>Describes a typed output parameter returned by a command factory.</summary>
public sealed class CommandOutput<T>(string name, string? sourceName = null) : CommandOutput(name, sourceName) {
    /// <summary>Gets the output value type.</summary>
    public override Type ValueType => typeof(T);
}

internal readonly record struct ResultParameter(string Name, Type ValueType, string? MemberName);
