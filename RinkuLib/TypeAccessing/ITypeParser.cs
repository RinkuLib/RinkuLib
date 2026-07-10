using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// Contract to update internal cache
/// </summary>
public interface ICache {
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// </summary>
    void UpdateCache(IDbCommand cmd);
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// </summary>
    Task UpdateCacheAsync(IDbCommand cmd, CancellationToken ct = default);
}
/// <summary>
/// Contract to update internal cache
/// </summary>
public interface ICacheGivingParser<T> {
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// And return a <see cref="ITypeParser{T}"/> using the reader
    /// </summary>
    ITypeParser<T> UpdateCache(IDbCommand cmd, DbDataReader reader);
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// And return a <see cref="ITypeParser{T}"/> using the reader
    /// </summary>
    ValueTask<ITypeParser<T>> UpdateCacheAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default);
    /// <summary>Returns the associated command behavior of the parser</summary>
    CommandBehavior Behavior { get; }
}
/// <summary>
/// The basic interface that parses a type from a db command
/// </summary>
public interface ITypeParser {
    /// <summary>Indicate the default <see cref="CommandBehavior"/> that can be use to call <see cref="IDbCommand.ExecuteReader(CommandBehavior)"/></summary>
    /// <remarks>May be something like <see cref="CommandBehavior.SequentialAccess"/> or <see cref="CommandBehavior.SingleResult"/></remarks>
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
/// The basic interface that parses a type from a db command
/// </summary>
public interface ITypeParser<T> : ITypeParser {
    internal bool InternalProtect { get; }
    /// <summary></summary>
    public T Default();
    /// <summary>
    /// Parses one <typeparamref name="T"/> starting at the current row, advancing the reader as it goes.
    /// <c>CanContinue</c> reports the state of the reader on return, <see langword="true"/> when it is
    /// positioned on an untreated row, <see langword="false"/> when no row is left
    /// </summary>
    public (bool CanContinue, T Result) Parse(DbDataReader reader);
    /// <inheritdoc cref="Parse"/>
    public ValueTask<(bool CanContinue, T Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default);


    /// <summary></summary>
    public T Query(DbCommand command, bool disposeCommand = false);
    /// <summary></summary>
    public T Query(IDbCommand command, bool disposeCommand = false);
    /// <summary></summary>
    public Task<T> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default);
    /// <summary></summary>
    public Task<T> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default);


    /// <summary></summary>
    public T Query(DbCommand command, ICache cache, bool disposeCommand = false);
    /// <summary></summary>
    public T Query(IDbCommand command, ICache cache, bool disposeCommand = false);
    /// <summary></summary>
    public Task<T> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
    /// <summary></summary>
    public Task<T> QueryAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
}
/// <summary>
/// A <see cref="ITypeParser{T}"/> that needs init before being used
/// </summary>
public interface IInitableTypeParser<T> : ITypeParser<T> {
    /// <summary></summary>
    public void Init(IDbCommand cmd, DbDataReader reader);
    /// <summary></summary>
    public Task InitAsync(IDbCommand cmd, DbDataReader reader, CancellationToken ct = default);
}
/// <summary></summary>
public static class EnumHelper {
    /// <summary></summary>
    public async static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> items, [EnumeratorCancellation] CancellationToken ct = default) {
        foreach (var item in items) {
            yield return item;
            ct.ThrowIfCancellationRequested();
        }
    }
}