using System.Data;
using System.Data.Common;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// Optionaly parse a type.
/// </summary>
public sealed class OptionalTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => true;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override TOpt Parse(DbDataReader reader)
        => TOpt.Make(ElementParser.Parse(reader));
    /// <inheritdoc/>
    public override async Task<TOpt> ParseAsync(DbDataReader reader, CancellationToken ct = default)
        => TOpt.Make(await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false));
}
/// <summary>Optimized List parser that uses a direct delegate.</summary>
public sealed class FastOptionalTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => false;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override TOpt Parse(DbDataReader reader)
        => TOpt.Make(Parser(reader));
    /// <inheritdoc/>
    public override Task<TOpt> ParseAsync(DbDataReader reader, CancellationToken ct = default)
        => Task.FromResult(TOpt.Make(Parser(reader)));
}
/// <summary>
/// Optionaly parse a type.
/// </summary>
public sealed class SingleTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => true;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override TOpt Parse(DbDataReader reader) { 
        var res = TOpt.Make(ElementParser.Parse(reader));
        if (reader.Read())
            throw new Exception("The query provided more result than required for the single item");
        return res;
    }
    /// <inheritdoc/>
    public override async Task<TOpt> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = TOpt.Make(await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false));
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new Exception("The query provided more result than required for the single item");
        return res;
    }
}
/// <summary>Optimized List parser that uses a direct delegate.</summary>
public sealed class FastSingleTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => true;
    /// <inheritdoc/>
    public override TOpt Default() => default;
    /// <inheritdoc/>
    public override TOpt Parse(DbDataReader reader) { 
        var res = TOpt.Make(Parser(reader));
        if (reader.Read())
            throw new Exception("The query provided more result than required for the single item");
        return res;
    }
    /// <inheritdoc/>
    public override async Task<TOpt> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = TOpt.Make(Parser(reader));
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new Exception("The query provided more result than required for the single item");
        return res;
    }
}