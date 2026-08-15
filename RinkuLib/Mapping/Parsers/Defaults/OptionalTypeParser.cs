using System.Data;
using System.Data.Common;
namespace Rinku.Mapping.Parsers.Defaults;
/// <summary>
/// The parser behind the optional shapes (<see cref="Optional{T}"/> and kin). It wraps an element parser and
/// turns a missing row into that shape's empty value instead of throwing.
/// </summary>
public sealed class OptionalTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior;
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) => ElementParser.CanParse(schema);
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
/// <summary>Builds an optional value with a row delegate that does not advance the reader.</summary>
public sealed class FastOptionalTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser, ITypeParser schemaParser) : BaseTypeParser<TOpt>, ISimpleParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    private readonly ITypeParser SchemaParser = schemaParser;
    /// <inheritdoc/>
    public Func<DbDataReader, TOpt> RowParser { get; } = r => TOpt.Make(parser(r));
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior;
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) => SchemaParser.CanParse(schema);
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
file static class SingleShape {
    internal static RinkuShapeException MoreThanOne()
        => new("The query returned more than one result for a single-result shape");
}
/// <summary>
/// A base for a wrapper that accepts no more than one result.
/// Override <see cref="BaseTypeParser{T}.Default"/> to choose what an empty query returns.
/// </summary>
public abstract class BaseSingleTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly ITypeParser<T> ElementParser = elementParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) => ElementParser.CanParse(schema);
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var (canContinue, res) = ElementParser.Parse(reader);
        if (canContinue)
            throw SingleShape.MoreThanOne();
        return (false, TOpt.Make(res));
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var (canContinue, res) = await ElementParser.ParseAsync(reader, ct).ConfigureAwait(false);
        if (canContinue)
            throw SingleShape.MoreThanOne();
        return (false, TOpt.Make(res));
    }
}

/// <summary>Requires exactly one result.</summary>
public sealed class SingleTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseSingleTypeParser<TOpt, T>(elementParser) where TOpt : struct, IWrapping<TOpt, T> {
    /// <inheritdoc/>
    public override TOpt Default() => throw new RinkuNoRowsException();
}

/// <summary>Returns the default value for no result and refuses a second result.</summary>
public sealed class SingleOrDefaultTypeParser<TOpt, T>(ITypeParser<T> elementParser) : BaseSingleTypeParser<TOpt, T>(elementParser) where TOpt : struct, IWrapping<TOpt, T> {
    /// <inheritdoc/>
    public override TOpt Default() => default;
}

/// <summary>
/// A base for a single-result wrapper whose element has a row delegate.
/// Override <see cref="BaseTypeParser{T}.Default"/> to choose what an empty query returns.
/// </summary>
public abstract class BaseFastSingleTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser, ITypeParser schemaParser) : BaseTypeParser<TOpt> where TOpt : struct, IWrapping<TOpt, T> {
    private readonly Func<DbDataReader, T> Parser = parser;
    private readonly ITypeParser SchemaParser = schemaParser;
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = behavior & ~CommandBehavior.SingleRow;
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) => SchemaParser.CanParse(schema);
    /// <inheritdoc/>
    public override (bool CanContinue, TOpt Result) Parse(DbDataReader reader) {
        var res = TOpt.Make(Parser(reader));
        if (reader.Read())
            throw SingleShape.MoreThanOne();
        return (false, res);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, TOpt Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = TOpt.Make(Parser(reader));
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            throw SingleShape.MoreThanOne();
        return (false, res);
    }
}

/// <summary>Requires exactly one result through a row delegate.</summary>
public sealed class FastSingleTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser, ITypeParser schemaParser) : BaseFastSingleTypeParser<TOpt, T>(behavior, parser, schemaParser) where TOpt : struct, IWrapping<TOpt, T> {
    /// <inheritdoc/>
    public override TOpt Default() => throw new RinkuNoRowsException();
}

/// <summary>Returns the default value for no result through a row delegate and refuses a second result.</summary>
public sealed class FastSingleOrDefaultTypeParser<TOpt, T>(CommandBehavior behavior, Func<DbDataReader, T> parser, ITypeParser schemaParser) : BaseFastSingleTypeParser<TOpt, T>(behavior, parser, schemaParser) where TOpt : struct, IWrapping<TOpt, T> {
    /// <inheritdoc/>
    public override TOpt Default() => default;
}
