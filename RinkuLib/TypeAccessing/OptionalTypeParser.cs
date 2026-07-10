using System.Data;
using System.Data.Common;
namespace RinkuLib.TypeAccessing;
/// <summary>
/// The parser behind the optional shapes (<see cref="Optional{T}"/> and kin). It wraps an element parser and
/// turns a missing row into that shape's empty value instead of throwing.
/// </summary>
public sealed class OptionalTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var (canContinue, res) = ElementParser.Parse(reader);
        return (canContinue, TOpt.Make(res));
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var (canContinue, res) = await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false);
        return (canContinue, TOpt.Make(res));
    }
}
/// <summary>The <see cref="OptionalTypeParser{TOpt, T}"/> fast path, for an element read by a plain row delegate.</summary>
public sealed class FastOptionalTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<TOpt>, ISimpleParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public Func<DbDataReader, TOpt> RowParser { get; } = r => TOpt.Make(parser(r));
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var res = TOpt.Make(Parser(reader));
        return (reader.Read(), res);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = TOpt.Make(Parser(reader));
        return (await reader.ReadAsync(ct).ConfigureAwait(false), res);
    }
}
/// <summary>
/// The parser behind <see cref="Single{T}"/>. It reads one row and throws if the query returned more,
/// enforcing exactly one result.
/// </summary>
public sealed class SingleTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var (canContinue, res) = ElementParser.Parse(reader);
        if (canContinue)
            throw new Exception("The query provided more result than required for the single item");
        return (false, TOpt.Make(res));
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var (canContinue, res) = await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false);
        if (canContinue)
            throw new Exception("The query provided more result than required for the single item");
        return (false, TOpt.Make(res));
    }
}
/// <summary>The <see cref="SingleTypeParser{TOpt, T}"/> fast path, for an element read by a plain row delegate.</summary>
public sealed class FastSingleTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var res = TOpt.Make(Parser(reader));
        if (reader.Read())
            throw new Exception("The query provided more result than required for the single item");
        return (false, res);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = TOpt.Make(Parser(reader));
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new Exception("The query provided more result than required for the single item");
        return (false, res);
    }
}
