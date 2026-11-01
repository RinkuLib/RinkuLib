using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits a typed tracking list with direct executors.</summary>
public sealed class ExecutorCommitter<T, TInsertResult, TUpdateResult, TDeleteResult> : TrackingCommitterCore<DbConnection, DbCommitState, TInsertResult, TUpdateResult, TDeleteResult>
{
    private readonly Func<T, DbConnection, DbTransaction, TInsertResult>? _insert;
    private readonly Func<T, DbConnection, DbTransaction, TUpdateResult>? _update;
    private readonly Func<T, DbConnection, DbTransaction, TDeleteResult>? _delete;
    private readonly IsolationLevel? _isolationLevel;

    private ExecutorCommitter(Func<T, DbConnection, DbTransaction, TInsertResult>? insert, Func<T, DbConnection, DbTransaction, TUpdateResult>? update, Func<T, DbConnection, DbTransaction, TDeleteResult>? delete, IsolationLevel? isolationLevel)
    {
        _insert = insert;
        _update = update;
        _delete = delete;
        _isolationLevel = isolationLevel;
    }

    internal static ExecutorCommitter<T, TInsertResult, TUpdateResult, TDeleteResult> Create(Func<T, DbConnection, DbTransaction, TInsertResult>? insert, Func<T, DbConnection, DbTransaction, TUpdateResult>? update, Func<T, DbConnection, DbTransaction, TDeleteResult>? delete, IsolationLevel? isolationLevel)
        => new(insert, update, delete, isolationLevel);

    /// <inheritdoc/>
    protected override bool CanInsert => _insert is not null;
    /// <inheritdoc/>
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc/>
    protected override bool CanDelete => _delete is not null;
    /// <inheritdoc/>
    protected override DbCommitState StartCommit(DbConnection connection) => new(connection, _isolationLevel);
    /// <inheritdoc/>
    protected override TInsertResult Insert<TItem>(TItem item, DbCommitState state) => Execute(_insert ?? throw new InvalidOperationException("Insert operation is not configured."), item, state);
    /// <inheritdoc/>
    protected override TUpdateResult Update<TItem>(TItem item, DbCommitState state) => Execute(_update ?? throw new InvalidOperationException("Update operation is not configured."), item, state);
    /// <inheritdoc/>
    protected override TDeleteResult Delete<TItem>(TItem item, DbCommitState state) => Execute(_delete ?? throw new InvalidOperationException("Delete operation is not configured."), item, state);

    /// <inheritdoc/>
    protected override bool CommitState(DbCommitState state)
    {
        state.Commit();
        return true;
    }

    /// <inheritdoc/>
    protected override void FailCommit(DbCommitState state) => state.Rollback();

    private static TResult Execute<TItem, TResult>(Func<T, DbConnection, DbTransaction, TResult> executor, TItem item, DbCommitState state)
        => executor(CommitterRuntime.RequireItem<T, TItem>(item), state.Connection, state.Transaction);

}
