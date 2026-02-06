using System.Data;

namespace RinkuLib.Queries;
/// <summary>
/// Provides extension methods to start and/or populate a <see cref="IQueryBuilder"/> 
/// </summary>
public static class BuilderStarter {
    /// <summary>
    /// Start a <see cref="QueryBuilder"/>.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command)
        => new(command);
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/>.
    /// </summary>
    public static QueryBuilderCommand<T> StartBuilder<T>(this QueryCommand command, T cmd) where T : IDbCommand
        => new(command, cmd);
    /// <summary>
    /// Start a <see cref="QueryBuilder"/> and set usage with the <paramref name="values"/>
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command, params Span<(string, object)> values) { 
        var builder = new QueryBuilder(command);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/> and set usage with the <paramref name="values"/>
    /// </summary>
    public static QueryBuilderCommand<T> StartBuilder<T>(this QueryCommand command, T cmd, params Span<(string, object)> values) where T : IDbCommand {
        var builder = new QueryBuilderCommand<T>(command, cmd);
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
/// By default, items are not used and they are activated via the <see cref="Use(string, object)"/> or <see cref="Use(string)"/> methods. 
/// The builder translates semantic names (like "ActiveOnly") into the specific state 
/// tracking required by the underlying <see cref="QueryCommand"/>.
/// </remarks>
public readonly struct QueryBuilder(QueryCommand QueryCommand) : IQueryBuilder {
    /// <summary>
    /// A marker used to activate a condition that does not require an associated data value.
    /// </summary>
    public static readonly object Used = new();
    /// <summary> The underlying command definition. </summary>
    public readonly QueryCommand QueryCommand = QueryCommand;
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
    /// <inheritdoc/>
    public readonly void Reset()
        => Array.Clear(Variables, 0, Variables.Length);
    /// <inheritdoc/>
    public readonly void ResetSelects()
        => Array.Clear(Variables, 0, QueryCommand.EndSelect);
    /// <inheritdoc/>
    public readonly void Remove(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        Variables[ind] = null;
    }
    /// <inheritdoc/>
    public readonly void Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= QueryCommand.StartVariables)
            throw new ArgumentException(condition);
        Variables[ind] = Used;
    }
    /// <inheritdoc/>
    public void SafelyUse(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind >= 0 && ind < QueryCommand.StartVariables)
            Variables[ind] = Used;
    }
    /// <inheritdoc/>
    public readonly bool Use(string variable, object value) {
        var ind = QueryCommand.Mapper.GetIndex(variable);
        var i = ind - QueryCommand.StartVariables;
        if (i < 0) {
            if (ind < 0)
                return false;
            if (value is bool b) {
                if (!b)
                    return false;
                Variables[ind] = Used;
                return true;
            }
            return false;
        }
        Variables[ind] = value;
        return true;
    }
    /// <inheritdoc/>
    public readonly object? this[string condition] {
        get => Variables[QueryCommand.Mapper.GetIndex(condition)];
    }
    /// <inheritdoc/>
    public readonly object? this[int ind] {
        get => Variables[ind];
    }
    /// <inheritdoc/>
    public readonly int GetRelativeIndex(string key) {
        var ind = QueryCommand.Mapper.GetIndex(key);
        var nbBefore = 0;
        for (int i = 0; i < ind; i++)
            if (Variables[i] is not null)
                nbBefore++;
        return nbBefore;
    }
    /// <inheritdoc/>
    public readonly string GetQueryText()
        => QueryCommand.QueryText.Parse(Variables);
}
