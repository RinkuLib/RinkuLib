using System.Data;
using System.Data.Common;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// Defines the contract for an executable query unit, managing the transition 
/// from a state-snapshot to a configured database command.
/// </summary>
public interface IQueryCommand {
    /// <summary>
    /// Configures the <paramref name="cmd"/> using a full state-snapshot of the query.
    /// </summary>
    /// <param name="cmd">The command to be populated.</param>
    /// <param name="variables">
    /// A unified array representing the state of all query elements (Selects, Conditions, 
    /// Variables, and Handlers). The array layout must strictly adhere to the <see cref="Mapper"/>.
    /// </param>
    /// <returns>True if the command was successfully prepared.</returns>
    public bool SetCommand(IDbCommand cmd, object?[] variables);
    /// <summary>
    /// Performance-optimized overload for concrete <see cref="DbCommand"/> types.
    /// </summary>
    public bool SetCommand(DbCommand cmd, object?[] variables);
    /// <summary>
    /// Configures the <paramref name="cmd"/> using a full state-snapshot of the query.
    /// </summary>
    /// <param name="cmd">The command to be populated.</param>
    /// <param name="parameterObj">
    /// An object representing the state of all query elements (Selects, Conditions, 
    /// Variables, and Handlers).</param>
    /// <param name="usageMap">The map of which item are used</param>
    /// <returns>True if the command was successfully prepared.</returns>
    public bool SetCommand(IDbCommand cmd, object parameterObj, Span<bool> usageMap);
    /// <summary>
    /// Performance-optimized overload for concrete <see cref="DbCommand"/> types.
    /// </summary>
    public bool SetCommand(DbCommand cmd, object parameterObj, Span<bool> usageMap);
    /// <summary>
    /// Configures the <paramref name="cmd"/> using a full state-snapshot of the query.
    /// </summary>
    /// <param name="cmd">The command to be populated.</param>
    /// <param name="parameterObj">
    /// An object representing the state of all query elements (Selects, Conditions, 
    /// Variables, and Handlers).</param>
    /// <param name="usageMap">The map of which item are used</param>
    /// <returns>True if the command was successfully prepared.</returns>
    public bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <summary>
    /// Performance-optimized overload for concrete <see cref="DbCommand"/> types.
    /// </summary>
    public bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <summary>
    /// Configures the <paramref name="cmd"/> using a full state-snapshot of the query.
    /// </summary>
    /// <param name="cmd">The command to be populated.</param>
    /// <param name="parameterObj">
    /// An object representing the state of all query elements (Selects, Conditions, 
    /// Variables, and Handlers).</param>
    /// <param name="usageMap">The map of which item are used</param>
    /// <returns>True if the command was successfully prepared.</returns>
    public bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <summary>
    /// Performance-optimized overload for concrete <see cref="DbCommand"/> types.
    /// </summary>
    public bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull;
    /// <summary> The shared coordinate system used to map names to array indices. </summary>
    public Mapper Mapper { get; }
    /// <summary> The index where literal-injection handlers begin. </summary>
    public int StartBaseHandlers { get; }
    /// <summary> The index where complex parameter handlers begin. </summary>
    public int StartSpecialHandlers { get; }
    /// <summary> The index where boolean toggle conditions begin. </summary>
    public int StartBoolCond { get; }
}
