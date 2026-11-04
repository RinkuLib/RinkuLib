using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Rinku;

public static partial class DatabaseCommandExtensions {
    /// <summary>Executes a query asynchronously and consumes its rows before disposing the command and database connection.</summary>
    public static Task<TResult> QueryAsync<T, TResult>(this IDatabase database, Func<DbConnection, DbCommand> factory, Func<IAsyncEnumerable<T>, CancellationToken, Task<TResult>> consume, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) => {
        await using var command = state.Factory(connection);
        return await state.Consume(command.StreamQueryAsync<T>(CachedTypeParser<T>.Default, disposeCommand: false, ct: token), token).ConfigureAwait(false);
    }, (Factory: factory, Consume: consume), DatabaseExtensions.Source(factory, source), cancellationToken);

    /// <summary>Executes a parameterized query asynchronously and consumes its rows within the database execution scope.</summary>
    public static Task<TResult> QueryAsync<T, T1, TResult>(this IDatabase database, Func<DbConnection, T1, DbCommand> factory, T1 argument1, Func<IAsyncEnumerable<T>, CancellationToken, Task<TResult>> consume, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) => {
        await using var command = state.Factory(connection, state.Arguments);
        return await state.Consume(command.StreamQueryAsync<T>(CachedTypeParser<T>.Default, disposeCommand: false, ct: token), token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: argument1, Consume: consume), DatabaseExtensions.Source(factory, source), cancellationToken);
}
