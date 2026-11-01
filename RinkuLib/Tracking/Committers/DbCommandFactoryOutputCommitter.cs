using System.Data.Common;

namespace Rinku.Tracking.Committers;

/// <summary>Commits generated command factories and applies inserted identity values only after SQL commits.</summary>
public sealed class DbCommandFactoryOutputCommitter<T, TInsertResult, TUpdateResult, TOutput, TCommandContext> : DbOutputCommitter<T, TInsertResult, TUpdateResult, int, TOutput>
{
    private readonly Func<T, DbConnection, (DbCommand Command, TCommandContext Context)> _insert;
    private readonly Func<T, DbConnection, DbCommand>? _update, _delete;
    private readonly Func<DbCommand, TUpdateResult> _readUpdate;
    private readonly Func<DbCommand, TInsertResult> _readInsert;
    private readonly Func<T, TCommandContext, TInsertResult, TOutput> _readOutput;
    private Func<T, DbCommand, TUpdateResult, TOutput>? _updateOutput;
    private Func<T, DbCommand, int, TOutput>? _deleteOutput;

    /// <summary>Stages values returned by an update operation.</summary>
    public DbCommandFactoryOutputCommitter<T, TInsertResult, TUpdateResult, TOutput, TCommandContext> UpdateOutput(Func<T, DbCommand, TUpdateResult, TOutput> readOutput) {
        ArgumentNullException.ThrowIfNull(readOutput);
        _updateOutput = readOutput;
        return this;
    }

    /// <summary>Stages values returned by a delete operation.</summary>
    public DbCommandFactoryOutputCommitter<T, TInsertResult, TUpdateResult, TOutput, TCommandContext> DeleteOutput(Func<T, DbCommand, int, TOutput> readOutput) {
        ArgumentNullException.ThrowIfNull(readOutput);
        _deleteOutput = readOutput;
        return this;
    }
    internal DbCommandFactoryOutputCommitter(Func<T, DbConnection, (DbCommand Command, TCommandContext Context)> insert, Func<DbCommand, TInsertResult> readInsert, Func<T, TCommandContext, TInsertResult, TOutput> readOutput, Action<T, TOutput> applyOutput, Func<DbCommand, TUpdateResult> readUpdate, Func<T, DbConnection, DbCommand>? update = null, Func<T, DbConnection, DbCommand>? delete = null) : base(applyOutput) {
        _insert = insert;
        _readUpdate = readUpdate;
        _readInsert = readInsert;
        _readOutput = readOutput;
        _update = update;
        _delete = delete;
    }
    /// <inheritdoc/>
    protected override bool CanUpdate => _update is not null;
    /// <inheritdoc/>
    protected override bool CanDelete => _delete is not null;
    /// <inheritdoc/>
    protected override TInsertResult Insert<TItem>(TItem item, DbOutputState<T, TOutput> state)
    {
        T outputItem = CommitterRuntime.RequireItem<T, TItem>(item);
        var created = _insert(outputItem, state.Database.Connection);
        using var command = created.Command;
        command.Transaction = state.Database.Transaction;
        TInsertResult result = _readInsert(command);
        state.Stage(outputItem, _readOutput(outputItem, created.Context, result));
        return result;
    }
    /// <inheritdoc/>
    protected override TUpdateResult Update<TItem>(TItem item, DbOutputState<T, TOutput> state) {
        T outputItem = CommitterRuntime.RequireItem<T, TItem>(item);
        using var command = _update!(outputItem, state.Database.Connection);
        command.Transaction = state.Database.Transaction;
        TUpdateResult result = _readUpdate(command);
        if (_updateOutput is { } readOutput)
            state.Stage(outputItem, readOutput(outputItem, command, result));
        return result;
    }
    /// <inheritdoc/>
    protected override int Delete<TItem>(TItem item, DbOutputState<T, TOutput> state) {
        T outputItem = CommitterRuntime.RequireItem<T, TItem>(item);
        using var command = _delete!(outputItem, state.Database.Connection);
        command.Transaction = state.Database.Transaction;
        int result = command.ExecuteNonQuery();
        if (_deleteOutput is { } readOutput)
            state.Stage(outputItem, readOutput(outputItem, command, result));
        return result;
    }
}
