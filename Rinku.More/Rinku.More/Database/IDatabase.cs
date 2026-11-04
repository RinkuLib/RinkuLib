using System.Data.Common;

namespace Rinku;

public interface IDatabase
{
    TResult Execute<TState, TResult>(Func<DbConnection, TState, TResult> invoke, TState state, string source);
    Task<TResult> ExecuteAsync<TState, TResult>(Func<DbConnection, TState, CancellationToken, Task<TResult>> invoke, TState state, string source, CancellationToken cancellationToken = default);
}
