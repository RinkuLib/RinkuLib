using System.Data;
using System.Data.Common;
namespace RinkuLib.TypeAccessing;
/// <summary>
/// The parser behind <see cref="List{T}"/>, it reads every row through an element parser and buffers them.
/// No row gives an empty list.
/// </summary>
public sealed class ListTypeParser<T>(ITypeParser<T> elementParser) : BaseTypeParser<List<T>> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override List<T> Default() => [];
    /// <inheritdoc/>
    public override (bool CanContinue, List<T> Result) Parse(DbDataReader reader) {
        var list = new List<T>();
        bool canContinue;
        do {
            (canContinue, var item) = ElementParser.Parse(reader);
            list.Add(item);
        } while (canContinue);
        return (false, list);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, List<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var list = new List<T>();
        bool canContinue;
        do {
            (canContinue, var item) = await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false);
            list.Add(item);
        } while (canContinue);
        return (false, list);
    }
}
/// <summary>The <see cref="ListTypeParser{T}"/> fast path, for elements read by a plain row delegate.</summary>
public sealed class FastListTypeParser<T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<List<T>> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override List<T> Default() => [];
    /// <inheritdoc/>
    public override (bool CanContinue, List<T> Result) Parse(DbDataReader reader) {
        var list = new List<T>();
        do { list.Add(Parser(reader)); } while (reader.Read());
        return (false, list);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, List<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var list = new List<T>();
        do { list.Add(Parser(reader)); } while (await reader.ReadAsync(ct).ConfigureAwait(false));
        return (false, list);
    }
}
