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
    /// <summary>
    /// Indicate if the implementation actualy needs async in it's async implementation
    /// </summary>
    public bool SupportsParsingAsync { get; }
}
/// <summary>
/// The basic interface that parses a type from a db command
/// </summary>
public interface ITypeParser<T> : ITypeParser {
    internal bool InternalProtect { get; }
    /// <summary></summary>
    public T Default();
    /// <summary></summary>
    public T Parse(DbDataReader reader);
    /// <summary></summary>
    public Task<T> ParseAsync(DbDataReader reader, CancellationToken ct = default);


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