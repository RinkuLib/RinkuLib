using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Parses a List of <typeparamref name="T"/> by repeatedly calling an element parser.
/// </summary>
public sealed class ListTypeParser<T>(ITypeParser<T> elementParser) : BaseTypeParser<List<T>> {
    private readonly ITypeParser<T> _elementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = (elementParser as IHasBehavior)?.Behavior ?? CommandBehavior.Default;
    /// <inheritdoc/>
    public override List<T> Default() => [];

    /// <inheritdoc/>
    public override List<T> Parse(DbDataReader reader) {
        var list = new List<T>();
        do {
            list.Add(_elementParser.Parse(reader));
        } while (reader.Read());

        return list;
    }

    /// <inheritdoc/>
    public override async Task<List<T>> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var list = new List<T>();
        do {
            list.Add(await _elementParser.ParseAsync(reader, ct).ConfigureAwait(false));
        } while (await reader.ReadAsync(ct).ConfigureAwait(false));

        return list;
    }
}