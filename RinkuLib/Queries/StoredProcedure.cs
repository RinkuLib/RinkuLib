using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RinkuLib.Queries;

/// <summary>
/// Reads what a stored procedure declares so a <see cref="QueryCommand"/> can bind it, the work behind
/// <see cref="QueryCommand.FromProc(string, IDbConnection)"/>.
/// </summary>
/// <remarks>
/// A procedure's parameters are known to the database and to nobody else, which is why one cannot be read
/// out of its own text the way a template can. Asking once, where the command is built, settles the names and
/// their metadata together, so a run binds each parameter with the type, size and direction the procedure
/// declared instead of leaving a first run to infer them.
/// </remarks>
public static class StoredProcedure {
    /// <summary>
    /// How a procedure's parameters are read onto a command. Every provider ships this as
    /// <c>DeriveParameters</c> on its command builder, and the default finds that one by the name the
    /// provider gave it, so this only needs setting for a provider that breaks the convention or to put
    /// something else in its place.
    /// </summary>
    public static Action<IDbCommand> ParameterDeriver = DeriveThroughProvider;

    private static (Type Command, Action<IDbCommand> Derive)[] Derivers = [];
    private static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        DeriverLock = new();

    /// <summary>
    /// The command for a procedure, reading what it declares through <see cref="ParameterDeriver"/>.
    /// </summary>
    /// <param name="connection">The connection to ask, opened for the question if it is not already.</param>
    /// <param name="procedureName">The procedure to call.</param>
    public static QueryCommand From(IDbConnection connection, string procedureName) {
        ArgumentNullException.ThrowIfNull(connection);
        bool opened = false;
        if (connection.State != ConnectionState.Open) {
            connection.Open();
            opened = true;
        }
        try {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = procedureName;
            cmd.CommandType = CommandType.StoredProcedure;
            ParameterDeriver(cmd);
            return FromCommand(cmd);
        }
        finally {
            if (opened)
                connection.Close();
        }
    }
    /// <summary>
    /// The command for a procedure, reading what it declares through <see cref="ParameterDeriver"/>, opening
    /// the connection asynchronously when it is not already open.
    /// </summary>
    /// <param name="connection">The connection to ask, opened for the question if it is not already.</param>
    /// <param name="procedureName">The procedure to call.</param>
    /// <param name="ct">The forwarded cancellation token.</param>
    public static async Task<QueryCommand> FromAsync(DbConnection connection, string procedureName, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(connection);
        bool opened = false;
        if (connection.State != ConnectionState.Open) {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            opened = true;
        }
        try {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = procedureName;
            cmd.CommandType = CommandType.StoredProcedure;
            ParameterDeriver(cmd);
            return FromCommand(cmd);
        }
        finally {
            if (opened)
                connection.Close();
        }
    }
    /// <summary>
    /// The command for a procedure whose parameters are already on <paramref name="command"/>, put there by
    /// a deriver or by hand. They are taken as the procedure's declaration, for their names and for how each
    /// one binds.
    /// </summary>
    /// <param name="command">A command naming the procedure, carrying its parameters.</param>
    public static QueryCommand FromCommand(IDbCommand command) {
        ArgumentNullException.ThrowIfNull(command);
        var parameters = command.Parameters;
        var names = new List<string>(parameters.Count);
        var infos = new List<DbParamInfo>(parameters.Count);
        for (int i = 0; i < parameters.Count; i++) {
            if (parameters[i] is not IDbDataParameter p)
                throw new RinkuBindingException(ErrorCodes.InvalidParameterAtIndex,
                    $"there is no valid parameter at index {i}");
            if (p.Direction == ParameterDirection.ReturnValue)
                continue;
            names.Add(p.ParameterName);
            infos.Add(DefaultParamCache.MakeDeclaredInfo(p));
        }
        var proc = new QueryCommand(command.CommandText, names, CommandType.StoredProcedure);
        for (int i = 0; i < names.Count; i++)
            proc.UpdateParamCache(names[i], infos[i]);
        return proc;
    }
    /// <summary>
    /// Reads a procedure through the command builder the provider ships beside its command, which is named
    /// for it: a <c>SqlCommand</c> is read by <c>SqlCommandBuilder</c>, an <c>NpgsqlCommand</c> by
    /// <c>NpgsqlCommandBuilder</c>. The one found for a command type is kept, so the search happens once.
    /// </summary>
    public static void DeriveThroughProvider(IDbCommand command) {
        ArgumentNullException.ThrowIfNull(command);
        var type = command.GetType();
        var current = Derivers;
        for (int i = 0; i < current.Length; i++)
            if (current[i].Command == type) {
                current[i].Derive(command);
                return;
            }
        lock (DeriverLock) {
            for (int i = 0; i < Derivers.Length; i++)
                if (Derivers[i].Command == type) {
                    Derivers[i].Derive(command);
                    return;
                }
            var derive = FindDeriver(type);
            Derivers = [.. Derivers, (type, derive)];
            derive(command);
        }
    }
    private static Action<IDbCommand> FindDeriver(Type commandType) {
        var name = commandType.FullName ?? commandType.Name;
        if (!name.EndsWith("Command", StringComparison.Ordinal))
            throw NoDeriver(commandType, "its name does not end in \"Command\"");
        var builder = commandType.Assembly.GetType(name + "Builder");
        var method = builder?.GetMethod("DeriveParameters", BindingFlags.Public | BindingFlags.Static, null, [commandType], null);
        if (method is null)
            throw NoDeriver(commandType, $"no public static {name}Builder.DeriveParameters was found beside it");
        return cmd => method.Invoke(null, [cmd]);
    }
    private static RinkuConfigurationException NoDeriver(Type commandType, string why)
        => new(ErrorCodes.OperationNotSupportedForType,
            $"cannot read a procedure's parameters for {commandType.FullName}, {why}. "
            + $"Set {nameof(StoredProcedure)}.{nameof(ParameterDeriver)} to a reader for this provider, "
            + "or name the parameters yourself on the command.");
}
