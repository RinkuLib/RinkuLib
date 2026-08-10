namespace Rinku.Mapping.Emission;

/// <summary>A bound target retained by emitted parser IL that can compare and release equivalent candidates.</summary>
internal interface IGeneratedParserTarget : IDisposable {
    /// <summary>Whether another emitted target represents the same bound state.</summary>
    bool Matches(object? other);
}
