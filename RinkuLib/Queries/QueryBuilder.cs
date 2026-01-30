using System.Data;

namespace RinkuLib.Queries;
public static class BuilderStarter {
    public static QueryBuilder<QueryCommand> StartBuilder(this QueryCommand command)
        => new(command);
    public static QueryBuilderCommand<QueryCommand, T> StartBuilder<T>(this QueryCommand command, T cmd) where T : IDbCommand
        => new(command, cmd);
    public static QueryBuilder<QueryCommand> StartBuilderWith(this QueryCommand command, params Span<(string, object)> values) { 
        var builder = new QueryBuilder<QueryCommand>(command);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    public static QueryBuilderCommand<QueryCommand, T> StartBuilderWith<T>(this QueryCommand command, T cmd, params Span<(string, object)> values) where T : IDbCommand {
        var builder = new QueryBuilderCommand<QueryCommand, T>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
}
/// <summary>
/// A stateful builder for configuring a specific query execution.
/// </summary>
/// <remarks>
/// This struct manages a state map used to decide which parts of a query are active. 
/// By default, items are not used; they are activated via the <see cref="Use"/> methods. 
/// The builder translates semantic names (like "ActiveOnly") into the specific state 
/// tracking required by the underlying <see cref="QueryCommand"/>.
/// </remarks>
public readonly struct QueryBuilder<TQueryCmd>(TQueryCmd QueryCommand) : IQueryBuilder where TQueryCmd : QueryCommand {
    /// <summary> The underlying command definition. </summary>
    public readonly TQueryCmd QueryCommand = QueryCommand;
    /// <summary> 
    /// The state-snapshot that drives SQL generation.
    /// <list type="bullet">
    /// <item><b>Binary Items (Selects/Conditions):</b> 
    /// Indices 0 to <see cref="QueryCommand.StartVariables"/> - 1. 
    /// These signify presence only and carry no data.</item>
    /// <item><b>Data Items (Variables/Handlers):</b> 
    /// Indices <see cref="QueryCommand.StartVariables"/> to Count - 1. 
    /// These require a value to be functional.</item>
    /// </list>
    /// </summary>
    public readonly object?[] Variables = new object?[QueryCommand.Mapper.Count];
    public readonly void Reset()
        => Array.Clear(Variables);
    public readonly void ResetSelects()
        => Array.Clear(Variables, 0, QueryCommand.EndSelect);
    public readonly void Remove(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        Variables[ind] = null;
    }
    public readonly void Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= QueryCommand.StartVariables)
            throw new ArgumentException(condition);
        Variables[ind] = IQueryBuilder.Used;
    }
    public void SafelyUse(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= 0 && ind < QueryCommand.StartVariables)
            Variables[ind] = IQueryBuilder.Used;
    }
    public readonly bool Use(string variable, object value) {
        var ind = QueryCommand.Mapper.GetIndex(variable);
        var i = ind - QueryCommand.StartVariables;
        if (i < 0) {
            if (ind < 0)
                return false;
            if (value is bool b) {
                if (!b)
                    return false;
                Variables[ind] = IQueryBuilder.Used;
                return true;
            }
            return false;
        }
        Variables[ind] = value;
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
