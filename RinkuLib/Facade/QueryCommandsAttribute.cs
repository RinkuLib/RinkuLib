namespace Rinku;

/// <summary>Places explicitly included SQL sources under a generated partial class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class QueryCommandsAttribute(string path) : Attribute {
    /// <summary>Gets the source file or directory, relative to the project or absolute.</summary>
    public string Path { get; } = path;
    /// <summary>Gets or sets a regular expression matched against relative SQL paths.</summary>
    public string? Pattern { get; set; }
    /// <summary>Gets or sets whether directory selection includes subdirectories.</summary>
    public bool Recursive { get; set; }
}
