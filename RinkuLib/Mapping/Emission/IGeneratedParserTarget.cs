namespace Rinku.Mapping.Emission;

internal interface IGeneratedParserTarget : IDisposable {
    bool Matches(object? other);
}
