using System.Data;
using System.Data.Common;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// What it takes to turn a query and a set of values into a ready-to-run database command. The execution
/// methods build on this, most code runs a <see cref="QueryCommand"/> through those rather than calling here.
/// </summary>
public interface IQueryCommand {
    /// <summary>
    /// Sets <paramref name="cmd"/>'s text and parameters for one run from a pre-built values array, the same
    /// array a <see cref="Commands.QueryBuilder"/> holds.
    /// </summary>
    /// <param name="cmd">The command to fill.</param>
    /// <param name="variables">The values for this run, one slot per key.</param>
    /// <returns><see langword="true"/> when the command is ready to run.</returns>
    public bool SetCommand(IDbCommand cmd, object?[] variables);
    /// <inheritdoc cref="SetCommand(IDbCommand, object[])"/>
    public bool SetCommand(DbCommand cmd, object?[] variables);
    /// <summary>
    /// Sets <paramref name="cmd"/>'s text and parameters for one run, reading the values from a parameter
    /// object matched to keys by name, and records which keys were used in <paramref name="usageMap"/>.
    /// </summary>
    /// <param name="cmd">The command to fill.</param>
    /// <param name="parameterObj">The object whose members supply the values.</param>
    /// <param name="usageMap">Filled with which keys ended up used, so the result can be read back correctly.</param>
    /// <returns><see langword="true"/> when the command is ready to run.</returns>
    public bool SetCommand(IDbCommand cmd, object parameterObj, Span<bool> usageMap);
    /// <inheritdoc cref="SetCommand(IDbCommand, object, Span{bool})"/>
    public bool SetCommand(DbCommand cmd, object parameterObj, Span<bool> usageMap);
    /// <inheritdoc cref="SetCommand(IDbCommand, object, Span{bool})"/>
    public bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <inheritdoc cref="SetCommand(IDbCommand, object, Span{bool})"/>
    public bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <inheritdoc cref="SetCommand(IDbCommand, object, Span{bool})"/>
    public bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <inheritdoc cref="SetCommand(IDbCommand, object, Span{bool})"/>
    public bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <summary> Maps the query's key names to their slot in the values array. </summary>
    public Mapper Mapper { get; }
    /// <summary> The first slot for a literal-injection handler. Slots below it carry parameter values. </summary>
    public int StartBaseHandlers { get; }
    /// <summary> The first slot for a special handler, the ones that expand into several parameters. </summary>
    public int StartSpecialHandlers { get; }
    /// <summary> The first slot for a toggle-only condition, the ones that carry no value. </summary>
    public int StartBoolCond { get; }
}
