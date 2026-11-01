using System.Data;
using System.Data.Common;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Committers;

/// <summary>Creates committers from reusable commands, command factories, or executors.</summary>
public static class Committer
{
    public static ProcessingCommitter<TItem> For<TItem>() => new();

    /// <summary>Creates a generated-command committer that assigns an inserted identity and clears transient members before confirmation.</summary>
    public static DbCommandFactoryOutputCommitter<T, object?, int, object?, DbCommand> Create<T>(Func<T, DbConnection, DbCommand> insert, string outputMember, Func<T, DbConnection, DbCommand>? update = null, Func<T, DbConnection, DbCommand>? delete = null, string[]? clearInsertMembers = null) where T : IRuntimeMemberAccess => new((item, connection) => { var command = insert(item, connection); return (command, command); }, static command => command.ExecuteScalar(), static (_, _, result) => result ?? throw new InvalidOperationException("The insert did not return an identity."), CommitterRuntime.OutputWriter<T, object?>(outputMember, clearInsertMembers), static command => command.ExecuteNonQuery(), update, delete);
    /// <summary>Combines an identity-producing command factory with a change-aware query update and an optional delete factory.</summary>
    public static DbCommandFactoryOutputCommitter<T, object?, int, object?, DbCommand> Create<T>(Func<T, DbConnection, DbCommand> insert, string outputMember, QueryCommand update, Func<T, DbConnection, DbCommand>? delete = null, string[]? clearInsertMembers = null) where T : ITrackingChanges, IRuntimeMemberAccess
        => new((item, connection) => { var command = insert(item, connection); return (command, command); }, static command => command.ExecuteScalar(), static (_, _, result) => result ?? throw new InvalidOperationException("The insert did not return an identity."), CommitterRuntime.OutputWriter<T, object?>(outputMember, clearInsertMembers), static command => command.ExecuteNonQuery(), (item, connection) => update.GetChangesCommand(item, connection), delete);

    public static DbCommandFactoryOutputCommitter<T, object?, TUpdateResult, object?, DbCommand> Create<T, TUpdateResult>(Func<T, DbConnection, DbCommand> insert, string outputMember, QueryCommand update, Func<DbCommand, TUpdateResult> readUpdate, Func<T, DbConnection, DbCommand>? delete = null, string[]? clearInsertMembers = null) where T : ITrackingChanges, IRuntimeMemberAccess
        => new((item, connection) => { var command = insert(item, connection); return (command, command); }, static command => command.ExecuteScalar(), static (_, _, result) => result ?? throw new InvalidOperationException("The insert did not return an identity."), CommitterRuntime.OutputWriter<T, object?>(outputMember, clearInsertMembers), readUpdate, (item, connection) => update.GetChangesCommand(item, connection), delete);

    public static DbCommandFactoryOutputCommitter<T, TResult, TResult, TOutput, DbCommand> Create<T, TResult, TOutput>(Func<T, DbConnection, DbCommand> insert, Func<DbCommand, TResult> readInsert, Func<DbCommand, TResult, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand update, Func<DbCommand, TResult> readUpdate, Func<T, DbConnection, DbCommand>? delete = null) where T : ITrackingChanges
        => new((item, connection) => { var command = insert(item, connection); return (command, command); }, readInsert, (_, command, result) => readOutput(command, result), applyOutput, readUpdate, (item, connection) => update.GetChangesCommand(item, connection), delete);
    /// <summary>Retains typed command outputs and constructs the insertion output before transaction commit.</summary>
    public static DbCommandFactoryOutputCommitter<T, TResult, TResult, TOutput, TCommandContext> Create<T, TResult, TOutput, TCommandContext>(Func<T, DbConnection, (DbCommand Command, TCommandContext Context)> insert, Func<DbCommand, TResult> readInsert, Func<T, TCommandContext, TResult, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand update, Func<DbCommand, TResult> readUpdate, Func<T, DbConnection, DbCommand>? delete = null) where T : ITrackingChanges
        => new(insert, readInsert, readOutput, applyOutput, readUpdate, (item, connection) => update.GetChangesCommand(item, connection), delete);

    /// <summary>Constructs an accepted original from a successful command before transaction commit.</summary>
    public static DbCommandFactoryOutputCommitter<T, int, int, TOriginal, DbCommand> Create<T, TOriginal>(Func<T, DbConnection, DbCommand> insert, Func<T, TOriginal> construct, Action<T, TOriginal> accept, Func<T, DbConnection, DbCommand>? update = null, Func<T, DbConnection, DbCommand>? delete = null)
        => new((item, connection) => { var command = insert(item, connection); return (command, command); }, static command => command.ExecuteNonQuery(), (item, _, _) => construct(item), accept, static command => command.ExecuteNonQuery(), update, delete);

    /// <summary>Combines arbitrary result reading and typed output construction with command factories.</summary>
    public static DbCommandFactoryOutputCommitter<T, TResult, int, TOutput, DbCommand> Create<T, TResult, TOutput>(Func<T, DbConnection, DbCommand> insert, Func<DbCommand, TResult> readInsert, Func<T, DbCommand, TResult, TOutput> construct, Action<T, TOutput> accept, Func<T, DbConnection, DbCommand>? update = null, Func<T, DbConnection, DbCommand>? delete = null)
        => new((item, connection) => { var command = insert(item, connection); return (command, command); }, readInsert, construct, accept, static command => command.ExecuteNonQuery(), update, delete);

    /// <summary>Creates a query committer that executes each configured command.</summary>
    public static QueryCommitter Create(QueryCommand? insert = null, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => QueryCommitter.Create(insert, update, delete, isolationLevel, timeout);

    /// <summary>Creates a committer whose insert command returns a scalar output.</summary>
    public static QueryScalarInsertCommitter<T, TOutput> Create<T, TOutput>(QueryCommand insert, Action<T, TOutput?> applyOutput, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => QueryScalarInsertCommitter<T, TOutput>.Create(insert, applyOutput, update, delete, isolationLevel, timeout);

    /// <summary>Creates a committer that assigns a scalar insertion output to a runtime member before confirmation.</summary>
    public static QueryScalarInsertCommitter<IRuntimeMemberAccess, TOutput> Create<TOutput>(QueryCommand insert, string outputMember, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputMember);
        return QueryScalarInsertCommitter<IRuntimeMemberAccess, TOutput>.Create(insert, (item, output) => CommitterRuntime.SetRuntimeMember(item, outputMember, output), update, delete, isolationLevel, timeout);
    }

    /// <summary>Creates a committer that reads an output from an executed insert command.</summary>
    public static QueryOutputInsertCommitter<T, TOutput> Create<T, TOutput>(QueryCommand insert, Func<DbCommand, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => QueryOutputInsertCommitter<T, TOutput>.Create(insert, readOutput, applyOutput, update, delete, isolationLevel, timeout);

    /// <summary>Creates a committer that assigns a command output to a runtime member before confirmation.</summary>
    public static QueryOutputInsertCommitter<IRuntimeMemberAccess, TOutput> Create<TOutput>(QueryCommand insert, Func<DbCommand, TOutput> readOutput, string outputMember, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputMember);
        return QueryOutputInsertCommitter<IRuntimeMemberAccess, TOutput>.Create(insert, readOutput, (item, output) => CommitterRuntime.SetRuntimeMember(item, outputMember, output), update, delete, isolationLevel, timeout);
    }

    /// <summary>Creates a committer that reads a named output parameter and assigns it to a runtime member.</summary>
    public static QueryOutputInsertCommitter<IRuntimeMemberAccess, TOutput> Create<TOutput>(QueryCommand insert, string outputParameter, string outputMember, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => Create(insert, CommitterRuntime.OutputReader<TOutput>(outputParameter), outputMember, update, delete, isolationLevel, timeout);

    /// <summary>Creates a committer that reads a named output parameter and applies it with a delegate.</summary>
    public static QueryOutputInsertCommitter<T, TOutput> Create<T, TOutput>(QueryCommand insert, string outputParameter, Action<T, TOutput> applyOutput, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => Create(insert, CommitterRuntime.OutputReader<TOutput>(outputParameter), applyOutput, update, delete, isolationLevel, timeout);

    /// <summary>Creates a command-factory committer.</summary>
    public static DbCommandFactoryCommitter<T> Create<T>(Func<T, DbConnection, DbCommand>? insert = null, Func<T, DbConnection, DbCommand>? update = null, Func<T, DbConnection, DbCommand>? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => DbCommandFactoryCommitter<T>.Create(insert, update, delete, isolationLevel, timeout);

    /// <summary>Creates a direct-executor committer.</summary>
    public static ExecutorCommitter<T, TInsertResult, TUpdateResult, TDeleteResult> Create<T, TInsertResult, TUpdateResult, TDeleteResult>(Func<T, DbConnection, DbTransaction, TInsertResult>? insert = null, Func<T, DbConnection, DbTransaction, TUpdateResult>? update = null, Func<T, DbConnection, DbTransaction, TDeleteResult>? delete = null, IsolationLevel? isolationLevel = null)
        => ExecutorCommitter<T, TInsertResult, TUpdateResult, TDeleteResult>.Create(insert, update, delete, isolationLevel);
}

/// <summary>Creates committers whose insert operation produces a specified result and an independent output.</summary>
public static class Committer<TResult>
{
    /// <summary>Creates a committer with an insertion result and an independent output.</summary>
    public static QueryResultOutputInsertCommitter<T, TResult, TOutput> Create<T, TOutput>(QueryCommand insert, Func<DbCommand, TOutput> readOutput, Action<T, TOutput> applyOutput, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => QueryResultOutputInsertCommitter<T, TResult, TOutput>.Create(insert, readOutput, applyOutput, update, delete, isolationLevel, timeout);

    /// <summary>Creates a result-producing committer that assigns its command output to a runtime member before confirmation.</summary>
    public static QueryResultOutputInsertCommitter<IRuntimeMemberAccess, TResult, TOutput> Create<TOutput>(QueryCommand insert, Func<DbCommand, TOutput> readOutput, string outputMember, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputMember);
        return QueryResultOutputInsertCommitter<IRuntimeMemberAccess, TResult, TOutput>.Create(insert, readOutput, (item, output) => CommitterRuntime.SetRuntimeMember(item, outputMember, output), update, delete, isolationLevel, timeout);
    }

    /// <summary>Creates a result-producing committer that reads a named output parameter and assigns it to a runtime member.</summary>
    public static QueryResultOutputInsertCommitter<IRuntimeMemberAccess, TResult, TOutput> Create<TOutput>(QueryCommand insert, string outputParameter, string outputMember, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => Create(insert, CommitterRuntime.OutputReader<TOutput>(outputParameter), outputMember, update, delete, isolationLevel, timeout);

    /// <summary>Creates a result-producing committer that reads a named output parameter and applies it with a delegate.</summary>
    public static QueryResultOutputInsertCommitter<T, TResult, TOutput> Create<T, TOutput>(QueryCommand insert, string outputParameter, Action<T, TOutput> applyOutput, QueryCommand? update = null, QueryCommand? delete = null, IsolationLevel? isolationLevel = null, int? timeout = null)
        => Create(insert, CommitterRuntime.OutputReader<TOutput>(outputParameter), applyOutput, update, delete, isolationLevel, timeout);
}
