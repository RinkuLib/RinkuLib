using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
namespace RinkuLib.TypeAccessing;
/// <summary>
/// The parser for a plain row type, one <typeparamref name="T"/> read straight from the current row by a
/// delegate. The common case behind a single object, a list element, or a scalar, with no row of its own to
/// look past.
/// </summary>
public sealed class SimpleTypeParser<T>(CommandBehavior Behavior, Func<DbDataReader, T> Parser) : BaseTypeParser<T>, ISimpleParser<T> {
    /// <inheritdoc/>
    public override CommandBehavior Behavior { get; } = Behavior;
    /// <summary>The delegate that reads one row into a <typeparamref name="T"/>.</summary>
    public readonly Func<DbDataReader, T> Parser = Parser;
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
