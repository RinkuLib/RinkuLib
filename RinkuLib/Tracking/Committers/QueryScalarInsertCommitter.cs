using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits scalar-producing insertions and applies their outputs before confirmation.</summary>
public sealed class QueryScalarInsertCommitter<T, TOutput> : DbOutputCommitter<T, bool, int, int, TOutput?>
{
    private readonly QueryCommand _insert;
    private readonly QueryCommand? _update;
    private readonly QueryCommand? _delete;

    private QueryScalarInsertCommitter(QueryCommand insert, Action<T, TOutput?> applyOutput, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout) : base(applyOutput, isolationLevel, timeout) {
        _insert = insert;
        _update = update;
        _delete = delete;
    }

    internal static QueryScalarInsertCommitter<T, TOutput> Create(QueryCommand insert, Action<T, TOutput?> applyOutput, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(applyOutput);

        return new(insert, applyOutput, update, delete, isolationLevel, timeout);
    }

    /// <inheritdoc/>
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc/>
    protected override bool CanDelete => _delete is not null;

    /// <inheritdoc/>
    protected override bool Insert<TItem>(TItem item, DbOutputState<T, TOutput?> state)
    {
        T outputItem = CommitterRuntime.RequireItem<T, TItem>(item);
        TOutput? output = _insert.ExecuteScalar<TOutput>(state.Database.Connection, (object?)item, state.Database.Transaction, state.Database.Timeout);
        state.Stage(outputItem, output);
        return true;
    }

    /// <inheritdoc/>
    protected override int Update<TItem>(TItem item, DbOutputState<T, TOutput?> state) => TrackingQueryExtensions.ExecuteUpdate(_update ?? throw new InvalidOperationException("Update operation is not configured."), item, state.Database);
    /// <inheritdoc/>
    protected override int Delete<TItem>(TItem item, DbOutputState<T, TOutput?> state) => Execute(_delete ?? throw new InvalidOperationException("Delete operation is not configured."), item, state);

    private static int Execute<TItem>(QueryCommand command, TItem item, DbOutputState<T, TOutput?> state) => command.Execute(state.Database.Connection, (object?)item, state.Database.Transaction, state.Database.Timeout);

}
