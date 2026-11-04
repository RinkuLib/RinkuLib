using System.Data.Common;

namespace Rinku;

public class ConnectionDatabase(Func<DbConnection> connect) : IDatabase
{
    public virtual TResult Execute<TState, TResult>(Func<DbConnection, TState, TResult> invoke, TState state, string source)
    {
        using var connection = connect();
        connection.Open();
        return invoke(connection, state);
    }

    public virtual async Task<TResult> ExecuteAsync<TState, TResult>(Func<DbConnection, TState, CancellationToken, Task<TResult>> invoke, TState state, string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = connect();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await invoke(connection, state, cancellationToken).ConfigureAwait(false);
    }
}
