using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.Queries;

/// <summary>
/// A stateful builder that actively synchronizes query state with a live database command.
/// </summary>
/// <remarks>
/// This struct manages the "active" state of a query. Unlike a standard builder, 
/// every call to <see cref="Use"/> or <see cref="Remove"/> immediately updates 
/// the underlying <see cref="Command"/> (e.g., adding, updating, or removing DB parameters).
/// </remarks>
public readonly struct QueryBuilderCommand<TCommand>(QueryCommand QueryCommand, TCommand Command) : IQueryBuilder where TCommand : IDbCommand {
    /// <summary> The underlying command definition. </summary>
    public readonly QueryCommand QueryCommand = QueryCommand;
    /// <summary> 
    /// The current state map. Changes here are mirrored in the <see cref="Command"/>'s parameter collection.
    /// </summary>
    public readonly object?[] Variables = new object?[QueryCommand.Mapper.Count];
    /// <summary> The live database command being synchronized. </summary>
    public readonly TCommand Command = Command;
    public readonly void Reset() {
        var varInfos = QueryCommand.Parameters._variablesInfo;
        ref object? pVar = ref Unsafe.Add(ref MemoryMarshal.GetReference(Variables), QueryCommand.StartVariables);
        for (int i = 0; i < varInfos.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pVar, i);
            if (currentVar is not null) {
                varInfos[i].Remove(Command, currentVar);
                currentVar = null;
            }
        }
        var handlers = QueryCommand.Parameters._specialHandlers;
        var nbSpecialHandlers = QueryCommand.Parameters.Total - varInfos.Length;
        ref object? pSpecialVar = ref Unsafe.Add(ref pVar, varInfos.Length);
        for (int i = 0; i < nbSpecialHandlers; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is not null) {
                handlers[i].Remove(Command, currentVar);
                currentVar = null;
            }
        }
    }
    public readonly void ResetSelects()
        => Array.Clear(Variables, 0, QueryCommand.EndSelect);
    public readonly void Remove(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartVariables) {
            if (ind > 0)
                Variables[ind] = null;
            return;
        }
        ref var val = ref Variables[ind];
        if (val is null)
            return;
        if (ind < QueryCommand.StartSpecialHandlers)
            QueryCommand.Parameters._variablesInfo[ind - QueryCommand.StartVariables].Remove(Command, val);
        else if (ind < QueryCommand.StartBaseHandlers)
            QueryCommand.Parameters._specialHandlers[ind - QueryCommand.StartSpecialHandlers].Remove(Command, val);
        val = null;
    }
    public readonly void Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= QueryCommand.StartVariables)
            throw new ArgumentException(condition);
        Variables[ind] = QueryBuilder.Used;
    }
    public void SafelyUse(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= 0 && ind < QueryCommand.StartVariables)
            Variables[ind] = QueryBuilder.Used;
    }
    /// <summary>
    /// Activates a variable and binds its data to the live <see cref="Command"/>.
    /// </summary>
    /// <returns>True if the variable was activated and the command parameter was created or updated.</returns>
    /// <remarks>
    /// This method performs one of three actions on the <see cref="Command"/>:
    /// <list type="bullet">
    /// <item><b>SaveUse:</b> If the variable was inactive, it creates and adds a new parameter.</item>
    /// <item><b>Update:</b> If the variable was already active, it updates the existing parameter value.</item>
    /// <item><b>Boolean Toggle:</b> If a <see cref="bool"/> true is passed to a non-data condition, it toggles it on.</item>
    /// </list>
    /// </remarks>
    public readonly bool Use(string variable, object value) {
        var ind = QueryCommand.Mapper.GetIndex(variable);
        var i = ind - QueryCommand.StartVariables;
        if (i < 0) {
            if (ind < 0)
                return false;
            if (value is bool b) {
                if (!b)
                    return false;
                Variables[ind] = QueryBuilder.Used;
                return true;
            }
            return false;
        }
        ref var val = ref Variables[ind];
        var key = QueryCommand.Mapper.GetKey(ind);
        if (val is null) {
            bool res;
            if (ind < QueryCommand.StartSpecialHandlers)
                res = QueryCommand.Parameters._variablesInfo[i].SaveUse(key, Command, ref value);
            else if (ind < QueryCommand.StartBaseHandlers)
                res = QueryCommand.Parameters._specialHandlers[ind - QueryCommand.StartSpecialHandlers].SaveUse(Command, ref value);
            else
                res = true;
            if (res)
                val = value;
            return res; 
        }
        if (ind < QueryCommand.StartSpecialHandlers)
            return QueryCommand.Parameters._variablesInfo[i].Update(Command, ref val, value);
        if (ind < QueryCommand.StartBaseHandlers)
            return QueryCommand.Parameters._specialHandlers[ind - QueryCommand.StartSpecialHandlers].Update(Command, ref val, value);
        val = value;
        return true;
    }
    public readonly object? this[string condition] {
        get => Variables[QueryCommand.Mapper.GetIndex(condition)];
    }
    public readonly object? this[int ind] {
        get => Variables[ind];
    }
    public readonly int GetRelativeIndex(string key) {
        var ind = QueryCommand.Mapper.GetIndex(key);
        var nbBefore = 0;
        for (int i = 0; i < ind; i++)
            if (Variables[i] is not null)
                nbBefore++;
        return nbBefore;
    }
    public readonly string GetQueryText()
        => QueryCommand.QueryText.Parse(Variables);
}
