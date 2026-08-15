using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Rinku.Mapping.Defaults;
using Rinku.Mapping.Emission;
namespace Rinku.Mapping.Parsers.Defaults;
/// <summary>
/// The parser for a plain row type, one <typeparamref name="T"/> read straight from the current row by a
/// delegate. The common case behind a single object, a list element, or a scalar, with no row of its own to
/// look past.
/// </summary>
public sealed class SimpleTypeParser<T> : BaseTypeParser<T>, ISimpleParser<T> {
    private readonly ParserSchema? Schema;
    private readonly EmissionFingerprint? Fingerprint;
    private readonly INullColHandler? GeneratedNullColHandler;
    internal INullColHandler GeneratedNullability => GeneratedNullColHandler!;
    /// <summary>Creates a parser that requires the supplied schema shape.</summary>
    public SimpleTypeParser(CommandBehavior behavior, Func<DbDataReader, T> parser, ColumnInfo[] schema) {
        Behavior = behavior;
        Parser = parser;
        Schema = ParserSchema.Exact(schema);
    }
    internal SimpleTypeParser(CommandBehavior behavior, Func<DbDataReader, T> parser, EmissionFingerprint fingerprint, INullColHandler generatedNullColHandler) {
        Behavior = behavior;
        Parser = parser;
        Fingerprint = fingerprint;
        GeneratedNullColHandler = generatedNullColHandler;
    }
    internal bool MatchesGenerated(CommandBehavior behavior, EmissionFingerprint fingerprint, object[] targets) => Behavior == behavior
        && Fingerprint == fingerprint && Parser.Target is object[] currentTargets
        && PreparedSimpleParser<T>.TargetsEqual(currentTargets, targets);
    /// <inheritdoc/>
    public override bool CanParse(ColumnInfo[] schema) {
        if (Schema is { } exactSchema && exactSchema.Accepts(schema))
            return true;
        return TypeParser.DefaultTypeParserMaker is ITypeParserSchemaMatcher matcher && matcher.CanParse(this, schema);
    }
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; }
    /// <summary>The delegate that reads one row into a <typeparamref name="T"/>.</summary>
    public readonly Func<DbDataReader, T> Parser;
    /// <inheritdoc/>
    public override void Dispose() {
        if (GeneratedNullColHandler is not null && Parser.Target is object[] targets)
            PreparedSimpleParser<T>.DisposeTargets(targets);
    }
    /// <inheritdoc/>
    public Func<DbDataReader, T> RowParser => Parser;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T Default() => throw new RinkuNoRowsException();
    /// <inheritdoc/>
    public override (bool CanContinue, T Result) Parse(DbDataReader reader) {
        var res = Parser(reader);
        return (reader.Read(), res);
    }
    /// <inheritdoc/>
    public override async ValueTask<(bool CanContinue, T Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var res = Parser(reader);
        return (await reader.ReadAsync(ct).ConfigureAwait(false), res);
    }
}
