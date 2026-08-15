using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Rinku.Mapping.Parsers;

/// <summary>Records parameter settings from a command after it has run.</summary>
public interface ICache {
    /// <summary>
    /// Learns from a command that has just run.
    /// </summary>
    void UpdateCache(IDbCommand cmd);
    /// <inheritdoc cref="UpdateCache"/>
    Task UpdateCacheAsync(IDbCommand cmd, CancellationToken ct = default);
}
/// <summary>Records parameter settings and gets a parser that accepts the reader columns.</summary>
public interface ICacheGivingParser<T> {
    /// <summary>
    /// Builds the parser for the reader's columns and records what the run taught about the command.
    /// </summary>
    ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader);
    /// <inheritdoc cref="UpdateCache"/>
    ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default);
    /// <summary>The reader behavior this parser wants when the command is executed.</summary>
    CommandBehavior Behavior { get; }
}
/// <summary>Describes a parser that reads query rows. Use <see cref="ITypeParser{T}"/> for a typed result.</summary>
public interface ITypeParser : IDisposable {
    /// <summary>Releases resources owned directly by this parser. Leave the default when it owns none.</summary>
    /// <remarks>
    /// This method must allow repeated calls. A wrapper must not dispose a child parser supplied to it.
    /// </remarks>
    void IDisposable.Dispose() { }
    /// <summary>Whether this parser can read a result carrying <paramref name="schema"/>.</summary>
    /// <remarks>
    /// A parser may accept several schemas. A parser that reads names and types at each call may accept every schema.
    /// </remarks>
    public bool CanParse(ColumnInfo[] schema);
    /// <summary>The reader behavior this parser wants, passed to <see cref="IDbCommand.ExecuteReader(CommandBehavior)"/>.</summary>
    /// <remarks>Often <see cref="CommandBehavior.SequentialAccess"/> or <see cref="CommandBehavior.SingleResult"/>.</remarks>
    public CommandBehavior Behavior { get; }
}
/// <summary>Reads the current row through a delegate and never advances the reader.</summary>
public interface ISimpleParser : ITypeParser {
    /// <summary>Gets the delegate that reads the current row without advancing the reader.</summary>
    Delegate RowParser { get; }
}
/// <summary>
/// Reads one complete value from consecutive rows. It starts on the first row and leaves the reader on the
/// final row used by that value. Do not use it when finding the end requires reading a later row.
/// </summary>
public interface IStepParser<T> : ITypeParser<T> {
    /// <summary>Parses one step. Enters on the step's first row, leaves the reader on the step's last row and never reads past it</summary>
    T ParseStep(DbDataReader reader);
    /// <inheritdoc cref="ParseStep"/>
    ValueTask<T> ParseStepAsync(DbDataReader reader, CancellationToken ct = default);
}
/// <summary>The typed form of <see cref="ISimpleParser"/>. It reads one row without advancing the reader.</summary>
public interface ISimpleParser<T> : ISimpleParser, IStepParser<T> {
    /// <summary>Gets the delegate that reads the current row without advancing the reader.</summary>
    new Func<DbDataReader, T> RowParser { get; }
    Delegate ISimpleParser.RowParser => RowParser;
    T IStepParser<T>.ParseStep(DbDataReader reader) => RowParser(reader);
    ValueTask<T> IStepParser<T>.ParseStepAsync(DbDataReader reader, CancellationToken ct) => new(RowParser(reader));
}
/// <summary>
/// Reads query rows as <typeparamref name="T"/>. Implement one to add a result shape that can be requested
/// through <c>Query&lt;T&gt;</c>.
/// </summary>
public interface ITypeParser<T> : ITypeParser {
    internal bool InternalProtect { get; }
    /// <summary>The value to return when the result has no row, an empty collection or optional, for instance.</summary>
    public T Default();
    /// <summary>
    /// Parses one <typeparamref name="T"/> starting at the current row, advancing the reader as it goes.
    /// <c>CanContinue</c> reports the state of the reader on return, <see langword="true"/> when it is
    /// positioned on an untreated row, <see langword="false"/> when no row is left
    /// </summary>
    public (bool CanContinue, T Result) Parse(DbDataReader reader);
    /// <inheritdoc cref="Parse"/>
    public ValueTask<(bool CanContinue, T Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default);


    /// <summary>Runs <paramref name="command"/> and reads its result as <typeparamref name="T"/>, disposing the command afterward when <paramref name="disposeCommand"/> is set.</summary>
    public T Query(DbCommand command, bool disposeCommand = false);
    /// <inheritdoc cref="Query(DbCommand, bool)"/>
    public T Query(IDbCommand command, bool disposeCommand = false);
    /// <inheritdoc cref="Query(DbCommand, bool)"/>
    public Task<T> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default);
    /// <inheritdoc cref="Query(DbCommand, bool)"/>
    public Task<T> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default);


    /// <summary>Runs <paramref name="command"/> and reads its result as <typeparamref name="T"/>, also letting <paramref name="cache"/> learn from the executed command.</summary>
    public T Query(DbCommand command, ICache cache, bool disposeCommand = false);
    /// <inheritdoc cref="Query(DbCommand, ICache, bool)"/>
    public T Query(IDbCommand command, ICache cache, bool disposeCommand = false);
    /// <inheritdoc cref="Query(DbCommand, ICache, bool)"/>
    public Task<T> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
    /// <inheritdoc cref="Query(DbCommand, ICache, bool)"/>
    public Task<T> QueryAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
}
/// <summary>Helpers for bridging sequences into async streams.</summary>
public static class EnumHelper {
    /// <summary>Wraps a synchronous sequence as an <see cref="IAsyncEnumerable{T}"/>, honoring cancellation between items.</summary>
    public async static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> items, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var item in items) {
            yield return item;
            ct.ThrowIfCancellationRequested();
        }
    }
}
