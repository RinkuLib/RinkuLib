using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Parses a List of <typeparamref name="T"/> by repeatedly calling an element parser.
/// </summary>
public sealed class ListTypeParser<T>(ITypeParser<T> elementParser) : BaseTypeParser<List<T>> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => true;

    /// <inheritdoc/>
    public override List<T> Default() => [];

    /// <inheritdoc/>
    public override List<T> Parse(DbDataReader reader) {
        var list = new List<T>();
        do {
            list.Add(ElementParser.Parse(reader));
        } while (reader.Read());

        return list;
    }

    /// <inheritdoc/>
    public override async Task<List<T>> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var list = new List<T>();
        do {
            list.Add(await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false));
        } while (await reader.ReadAsync(ct).ConfigureAwait(false));

        return list;
    }
}
/// <summary>Optimized List parser that uses a direct delegate.</summary>
public sealed class FastListTypeParser<T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<List<T>> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => true;
    /// <inheritdoc/>
    public override List<T> Default() => [];
    /// <inheritdoc/>
    public override List<T> Parse(DbDataReader reader) {
        var list = new List<T>();
        do { list.Add(Parser(reader)); } while (reader.Read());
        return list;
    }
    /// <inheritdoc/>
    public override async Task<List<T>> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var list = new List<T>();
        do { list.Add(Parser(reader)); } while (await reader.ReadAsync(ct).ConfigureAwait(false));
        return list;
    }
}