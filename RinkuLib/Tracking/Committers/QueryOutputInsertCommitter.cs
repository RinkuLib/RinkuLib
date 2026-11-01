using System.Data;
using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits insertions with command outputs and applies those outputs before confirmation.</summary>
public sealed class QueryOutputInsertCommitter<T, TOutput> : DbOutputCommitter<T, int, int, int, TOutput>
{
    private readonly QueryCommand _insert;
    private readonly QueryCommand? _update;
    private readonly QueryCommand? _delete;
    private readonly Func<DbCommand, TOutput> _readOutput;

    private QueryOutputInsertCommitter(QueryCommand insert, Func<DbCommand, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout) : base(applyOutput, isolationLevel, timeout) {
        _insert = insert;
        _update = update;
        _delete = delete;
        _readOutput = readOutput;
    }

    internal static QueryOutputInsertCommitter<T, TOutput> Create(QueryCommand insert, Func<DbCommand, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand? update, QueryCommand? delete, IsolationLevel? isolationLevel, int? timeout)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(readOutput);
        ArgumentNullException.ThrowIfNull(applyOutput);

        return new(insert, readOutput, applyOutput, update, delete, isolationLevel, timeout);
    }

    /// <inheritdoc/>
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc/>
    protected override bool CanDelete => _delete is not null;

    /// <inheritdoc/>
    protected override int Insert<TItem>(TItem item, DbOutputState<T, TOutput> state)
    {
        T outputItem = CommitterRuntime.RequireItem<T, TItem>(item);
        int result = _insert.Execute(state.Database.Connection, out DbCommand command, (object?)item, state.Database.Transaction, state.Database.Timeout);

        using (command)
            state.Stage(outputItem, _readOutput(command));

        return result;
    }

    /// <inheritdoc/>
    protected override int Update<TItem>(TItem item, DbOutputState<T, TOutput> state) => TrackingQueryExtensions.ExecuteUpdate(_update ?? throw new InvalidOperationException("Update operation is not configured."), item, state.Database);
    /// <inheritdoc/>
    protected override int Delete<TItem>(TItem item, DbOutputState<T, TOutput> state) => Execute(_delete ?? throw new InvalidOperationException("Delete operation is not configured."), item, state);

    private static int Execute<TItem>(QueryCommand command, TItem item, DbOutputState<T, TOutput> state) => command.Execute(state.Database.Connection, (object?)item, state.Database.Transaction, state.Database.Timeout);

}
