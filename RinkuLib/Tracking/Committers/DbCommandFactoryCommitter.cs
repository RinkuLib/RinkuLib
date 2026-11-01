using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits a typed tracking list with item-to-command factories.</summary>
public sealed class DbCommandFactoryCommitter<T> : TrackingCommitterCore<DbConnection, DbCommitState, int, int, int>
{
    private readonly Func<T, DbConnection, DbCommand>? _insert;
    private readonly Func<T, DbConnection, DbCommand>? _update;
    private readonly Func<T, DbConnection, DbCommand>? _delete;
    private readonly IsolationLevel? _isolationLevel;
    private readonly int? _timeout;

    private DbCommandFactoryCommitter(Func<T, DbConnection, DbCommand>? insert, Func<T, DbConnection, DbCommand>? update, Func<T, DbConnection, DbCommand>? delete, IsolationLevel? isolationLevel, int? timeout)
    {
        _insert = insert;
        _update = update;
        _delete = delete;
        _isolationLevel = isolationLevel;
        _timeout = timeout;
    }

    internal static DbCommandFactoryCommitter<T> Create(Func<T, DbConnection, DbCommand>? insert, Func<T, DbConnection, DbCommand>? update, Func<T, DbConnection, DbCommand>? delete, IsolationLevel? isolationLevel, int? timeout)
        => new(insert, update, delete, isolationLevel, timeout);

    /// <inheritdoc/>
    protected override bool CanInsert => _insert is not null;
    /// <inheritdoc/>
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc/>
    protected override bool CanDelete => _delete is not null;
    /// <inheritdoc/>
    protected override DbCommitState StartCommit(DbConnection connection) => new(connection, _isolationLevel, _timeout);
    /// <inheritdoc/>
    protected override int Insert<TItem>(TItem item, DbCommitState state) => Execute(_insert ?? throw new InvalidOperationException("Insert operation is not configured."), item, state);
    /// <inheritdoc/>
    protected override int Update<TItem>(TItem item, DbCommitState state) => Execute(_update ?? throw new InvalidOperationException("Update operation is not configured."), item, state);
    /// <inheritdoc/>
    protected override int Delete<TItem>(TItem item, DbCommitState state) => Execute(_delete ?? throw new InvalidOperationException("Delete operation is not configured."), item, state);

    /// <inheritdoc/>
    protected override bool CommitState(DbCommitState state)
    {
        state.Commit();
        return true;
    }

    /// <inheritdoc/>
    protected override void FailCommit(DbCommitState state) => state.Rollback();

    private static int Execute<TItem>(Func<T, DbConnection, DbCommand> factory, TItem item, DbCommitState state)
    {
        T typed = CommitterRuntime.RequireItem<T, TItem>(item);
        using DbCommand command = factory(typed, state.Connection);
        command.Transaction = state.Transaction;

        if (state.Timeout is int timeout)
            command.CommandTimeout = timeout;

        return command.ExecuteNonQuery();
    }

}
