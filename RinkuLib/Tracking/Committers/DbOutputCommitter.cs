using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Owns transactional output buffering and applies accepted outputs before tracking confirmation.</summary>
public abstract class DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> : TrackingCommitterCore<DbConnection, DbOutputState<TItem, TOutput>, TInsertResult, TUpdateResult, TDeleteResult> {
    private Action<TItem, TOutput> _applyOutput;
    private readonly IsolationLevel? _isolationLevel;
    private readonly int? _timeout;
    private Func<TItem, TOutput, TOutput>? _prepare;
    private Func<TItem, bool>? _beforeEach;
    private Func<TItem, TInsertResult, bool>? _acceptInsert;
    private Func<TItem, TUpdateResult, bool>? _acceptUpdate;
    private Func<TItem, TDeleteResult, bool>? _acceptDelete;

    /// <summary>Prepares staged values after all results are accepted and before persistence commits.</summary>
    public DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> Prepare(Func<TItem, TOutput, TOutput> prepare) {
        ArgumentNullException.ThrowIfNull(prepare);
        _prepare = prepare;
        return this;
    }

    public virtual DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> BeforeEach(Action<TItem> before) => BeforeEach(item => { before(item); return true; });
    public virtual DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> BeforeEach(Func<TItem, bool> before) {
        ArgumentNullException.ThrowIfNull(before);
        var previous = _beforeEach;
        _beforeEach = previous is null ? before : item => previous(item) & before(item);
        return this;
    }
    public DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> AfterInsert(Func<TItem, TInsertResult, bool> after) { _acceptInsert = after; return this; }
    public DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> AfterUpdate(Func<TItem, TUpdateResult, bool> after) { _acceptUpdate = after; return this; }
    public DbOutputCommitter<TItem, TInsertResult, TUpdateResult, TDeleteResult, TOutput> AfterDelete(Func<TItem, TDeleteResult, bool> after) { _acceptDelete = after; return this; }

    protected override bool ValidateItem<T>(T item) => _beforeEach?.Invoke(CommitterRuntime.RequireItem<TItem, T>(item)) ?? true;
    protected override bool HandleInsertResult<T>(T item, TInsertResult result) => _acceptInsert?.Invoke(CommitterRuntime.RequireItem<TItem, T>(item), result) ?? true;
    protected override bool HandleUpdateResult<T>(T item, TUpdateResult result) => _acceptUpdate?.Invoke(CommitterRuntime.RequireItem<TItem, T>(item), result) ?? true;
    protected override bool HandleDeleteResult<T>(T item, TDeleteResult result) => _acceptDelete?.Invoke(CommitterRuntime.RequireItem<TItem, T>(item), result) ?? true;

    protected void SetAcceptance(Action<TItem, TOutput> accept) => _applyOutput = accept ?? throw new ArgumentNullException(nameof(accept));

    protected DbOutputCommitter(Action<TItem, TOutput> applyOutput, IsolationLevel? isolationLevel = null, int? timeout = null) {
        ArgumentNullException.ThrowIfNull(applyOutput);
        _applyOutput = applyOutput;
        _isolationLevel = isolationLevel;
        _timeout = timeout;
    }

    protected override DbOutputState<TItem, TOutput> StartCommit(DbConnection connection) => new(connection, _isolationLevel, _timeout);

    protected override bool CommitState(DbOutputState<TItem, TOutput> state) {
        if (_prepare is { } prepare)
            for (int index = 0; index < state.Outputs.Count; index++) {
                var (item, parameters) = state.Outputs[index];
                state.Outputs[index] = (item, prepare(item, parameters));
            }
        state.Database.Commit();
        return true;
    }

    protected override void FailCommit(DbOutputState<TItem, TOutput> state) {
        try {
            state.Database.Rollback();
        }
        finally {
            state.Reset();
        }
    }

    protected override void BeforeConfirm(DbOutputState<TItem, TOutput> state) {
        foreach (var (item, output) in state.Outputs)
            _applyOutput(item, output);
    }
}
