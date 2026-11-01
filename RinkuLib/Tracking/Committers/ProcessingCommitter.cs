using System.Data.Common;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Committers;

/// <summary>Runs persistence operations before asking tracking to confirm its own state.</summary>
public sealed class ProcessingCommitter<TItem> : TrackingCommitterCore<DbConnection, DbCommitState, bool, bool, bool> {
    private Func<TItem, DbCommitState, bool>? _insert, _update, _delete;
    private Func<TItem, bool>? _before;
    /// <summary>Registers an action that runs before each item.</summary>
    public ProcessingCommitter<TItem> BeforeEach(Action<TItem> before) => BeforeEach(item => { before(item); return true; });
    /// <summary>Registers a predicate that can reject an item before persistence.</summary>
    public ProcessingCommitter<TItem> BeforeEach(Func<TItem, bool> before) {
        var previous = _before;
        _before = previous is null ? before : item => previous(item) & before(item);
        return this;
    }
    /// <summary>Configures insertion processing.</summary>
    public ProcessingCommitter<TItem> Insert<TResult>(CommitOperation<TItem, TResult> operation) { _insert = operation.CompileProcessing(); return this; }
    /// <summary>Configures update processing.</summary>
    public ProcessingCommitter<TItem> Update<TResult>(CommitOperation<TItem, TResult> operation) { _update = operation.CompileProcessing(); return this; }
    /// <summary>Configures deletion processing.</summary>
    public ProcessingCommitter<TItem> Delete<TResult>(CommitOperation<TItem, TResult> operation) { _delete = operation.CompileProcessing(); return this; }
    /// <summary>Uses one operation for both insertion and update processing.</summary>
    public ProcessingCommitter<TItem> Upsert<TResult>(CommitOperation<TItem, TResult> operation) { _insert = _update = operation.CompileProcessing(); return this; }
    /// <inheritdoc />
    protected override bool CanInsert => _insert is not null;
    /// <inheritdoc />
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc />
    protected override bool CanDelete => _delete is not null;
    /// <inheritdoc />
    protected override bool ValidateItem<T>(T item) {
        var target = CommitterRuntime.RequireItem<TItem, T>(item);
        if (target is ITrackingParameters parameters) parameters.PendingParameters = null;
        return _before?.Invoke(target) ?? true;
    }
    /// <inheritdoc />
    protected override void ResetItemParameters<T>(T item) {
        if (item is ITrackingParameters parameters) parameters.PendingParameters = null;
    }
    /// <inheritdoc />
    protected override bool Insert<T>(T item, DbCommitState state) => _insert!(CommitterRuntime.RequireItem<TItem, T>(item), state);
    /// <inheritdoc />
    protected override bool Update<T>(T item, DbCommitState state) => _update!(CommitterRuntime.RequireItem<TItem, T>(item), state);
    /// <inheritdoc />
    protected override bool Delete<T>(T item, DbCommitState state) => _delete!(CommitterRuntime.RequireItem<TItem, T>(item), state);
    /// <inheritdoc />
    protected override bool HandleInsertResult<T>(T item, bool result) => result;
    /// <inheritdoc />
    protected override bool HandleUpdateResult<T>(T item, bool result) => result;
    /// <inheritdoc />
    protected override bool HandleDeleteResult<T>(T item, bool result) => result;
    /// <inheritdoc />
    protected override DbCommitState StartCommit(DbConnection connection) => new(connection);
    /// <inheritdoc />
    protected override bool CommitState(DbCommitState state) { state.Commit(); return true; }
    /// <inheritdoc />
    protected override void FailCommit(DbCommitState state) => state.Rollback();
}
