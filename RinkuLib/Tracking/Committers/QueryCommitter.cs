using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits a tracking list with reusable query commands.</summary>
public sealed class QueryCommitter : TrackingCommitterCore<DbConnection, DbCommitState, int, int, int>
{
    private readonly QueryCommand? _insert;
    private readonly QueryCommand? _update;
    private readonly QueryCommand? _delete;
    private readonly IsolationLevel? _isolationLevel;
    private readonly int? _timeout;

    private QueryCommitter(QueryCommand? insert, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout)
    {
        _insert = insert;
        _update = update;
        _delete = delete;
        _isolationLevel = isolationLevel;
        _timeout = timeout;
    }

    internal static QueryCommitter Create(QueryCommand? insert, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout)
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
    protected override int Update<TItem>(TItem item, DbCommitState state) => TrackingQueryExtensions.ExecuteUpdate(_update ?? throw new InvalidOperationException("Update operation is not configured."), item, state);
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

    private static int Execute<TItem>(QueryCommand command, TItem item, DbCommitState state) => command.Execute(state.Connection, (object?)item, state.Transaction, state.Timeout);

}
