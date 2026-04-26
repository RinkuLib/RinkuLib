using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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
}
/// <summary>
/// Contract to update internal cache
/// </summary>
public interface ICacheUsingParser<T> {
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// And return a <see cref="ITypeParser{T}"/> using the reader
    /// </summary>
    void UpdateCache(IDbCommand cmd, DbDataReader reader, [NotNull] ref ITypeParser<T>? parser);
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
/// A type parser that should take the disposing responsability
/// </summary>
public interface ILazyTypeParser<T> : ITypeParser<T> {
    /// <summary>
    /// Parser the result, but also dispose when ready
    /// </summary>
    T ParseAndOwn(DbDataReader reader, IDbCommand command, bool wasClosed, bool disposeCommand);
}
/// <summary>
/// The basic interface that parses a type from a db command
/// </summary>
public interface ITypeParser<T> : ITypeParser {
    internal bool InternalProtect { get; }
    /// <summary></summary>
    public T? Default();
    /// <summary></summary>
    public T Parse(DbDataReader reader);
    /// <summary></summary>
    public Task<T> ParseAsync(DbDataReader reader, CancellationToken ct = default);


    /// <summary></summary>
    public T? Query(DbCommand command, bool disposeCommand = false);
    /// <summary></summary>
    public T? Query(IDbCommand command, bool disposeCommand = false);
    /// <summary></summary>
    public Task<T?> QueryAsync(DbCommand command, bool disposeCommand = false, CancellationToken ct = default);
    /// <summary></summary>
    public Task<T?> QueryAsync(IDbCommand command, bool disposeCommand = false, CancellationToken ct = default);


    /// <summary></summary>
    public T? Query(DbCommand command, ICache cache, bool disposeCommand = false);
    /// <summary></summary>
    public T? Query(IDbCommand command, ICache cache, bool disposeCommand = false);
    /// <summary></summary>
    public Task<T?> QueryAsync(DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
    /// <summary></summary>
    public Task<T?> QueryAsync(IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default);
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