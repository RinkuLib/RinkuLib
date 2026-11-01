namespace Rinku.Tracking.Committers;

/// <summary>Names a typed output's destination in the processing parameter bag and its source on the command factory.</summary>
public abstract class CommandOutput {
    private protected CommandOutput(string name, string? sourceName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sourceName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        Name = name;
        SourceName = sourceName;
    }
    public string Name { get; }
    public string? SourceName { get; }
    public abstract Type ValueType { get; }
    public static CommandOutput<T> Named<T>(string name, string? sourceName = null) => new(name, sourceName);
}

public sealed class CommandOutput<T>(string name, string? sourceName = null) : CommandOutput(name, sourceName) {
    public override Type ValueType => typeof(T);
}

internal readonly record struct ResultParameter(string Name, Type ValueType, string? MemberName);
