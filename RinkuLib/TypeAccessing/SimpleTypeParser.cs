using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Class that parse a <typeparamref name="T"/> object from the db
/// </summary>
public sealed class SimpleTypeParser<T>(CommandBehavior Behavior, Func<DbDataReader, T> Parser) : BaseTypeParser<T> {
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = Behavior;
    /// <summary>The actual function that do the parsing</summary>
    public readonly Func<DbDataReader, T> Parser = Parser;
    /// <inheritdoc/>
    public override bool SupportsParsingAsync => false;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T Default() => throw new Exception("No values were returned from the query");
    /// <inheritdoc/>
    public override T Parse(DbDataReader reader) => Parser(reader);
    /// <inheritdoc/>
    public override Task<T> ParseAsync(DbDataReader reader, CancellationToken ct = default) => Task.FromResult(Parser(reader));
}