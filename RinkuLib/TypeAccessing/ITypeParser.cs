using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Something that learns from a command once it has run. A command that has just executed carries the
/// provider's real parameter metadata, and this hands it a chance to record it so later runs skip the
/// discovery.
/// </summary>
public interface ICache {
    /// <summary>
    /// Learns from a command that has just run.
    /// </summary>
    void UpdateCache(IDbCommand cmd);
    /// <inheritdoc cref="UpdateCache"/>
    Task UpdateCacheAsync(IDbCommand cmd, CancellationToken ct = default);
}
/// <summary>
/// Learns from a command that has just run and, from its reader, hands back the parser for the result. The
/// first-run bridge that turns a command's columns into a reusable <see cref="ITypeParser{T}"/>.
/// </summary>
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
/// <summary>
/// The base of every parser, the compiled reader from a result's rows to a value. Ask for a
/// <see cref="ITypeParser{T}"/> for the typed form.
/// </summary>
public interface ITypeParser {
    /// <summary>The reader behavior this parser wants, passed to <see cref="IDbCommand.ExecuteReader(CommandBehavior)"/>.</summary>
    /// <remarks>Often <see cref="CommandBehavior.SequentialAccess"/> or <see cref="CommandBehavior.SingleResult"/>.</remarks>
    public CommandBehavior Behavior { get; }
}
/// <summary>
/// A parser that only uses the current row, does synchronous work and is reducible to a delegate.
/// The delegate never advances the reader, callers of the delegate own the advance.
/// </summary>
public interface ISimpleParser : ITypeParser {
    /// <summary>The row delegate, a <see cref="Func{T, TResult}"/> of <see cref="DbDataReader"/> to the parsed type. It parses the current row and never advances the reader</summary>
    Delegate RowParser { get; }
}
/// <summary>
/// A parser whose value is one self-delimited step of rows. It is called on the first row of the step,
/// leaves the reader on the last row of it and never reads further, so the caller owns the advance
/// between steps. The rows a step takes are decided by the parser alone; a parser that must look past
/// its own rows to find its end (a List gathering every row) cannot be one
/// </summary>
public interface IStepParser<T> : ITypeParser<T> {
    /// <summary>Parses one step. Enters on the step's first row, leaves the reader on the step's last row and never reads past it</summary>
    T ParseStep(DbDataReader reader);
    /// <inheritdoc cref="ParseStep"/>
    ValueTask<T> ParseStepAsync(DbDataReader reader, CancellationToken ct = default);
}
/// <summary>
/// The typed counterpart of <see cref="ISimpleParser"/>. A row-local parser is a one row step,
/// so it is an <see cref="IStepParser{T}"/> whose step never advances the reader at all
/// </summary>
public interface ISimpleParser<T> : ISimpleParser, IStepParser<T> {
    /// <summary>The row delegate. It parses the current row and never advances the reader</summary>
    new Func<DbDataReader, T> RowParser { get; }
    Delegate ISimpleParser.RowParser => RowParser;
    T IStepParser<T>.ParseStep(DbDataReader reader) => RowParser(reader);
    ValueTask<T> IStepParser<T>.ParseStepAsync(DbDataReader reader, CancellationToken ct) => new(RowParser(reader));
}
/// <summary>
/// The compiled reader from a result's rows to a <typeparamref name="T"/>. A result shape is just a
/// <typeparamref name="T"/> with its own parser, which is how one <c>Query&lt;T&gt;</c> covers a single row,
/// a list, an optional, and the rest. The <c>Query</c> methods run a command through this parser.
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
/// <summary>
/// A parser that must see the reader before it can work, for a shape whose plan depends on the actual columns.
/// </summary>
public interface IInitableTypeParser<T> : ITypeParser<T> {
    /// <summary>Prepares the parser from the command and its reader, before the first row is read.</summary>
    public void Init(IDbCommand cmd, DbDataReader reader);
    /// <inheritdoc cref="Init"/>
    public Task InitAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default);
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