using System.Collections;
using System.Data;
using System.Data.Common;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Provides extension methods to start and/or populate a <see cref="IQueryBuilder"/> 
/// </summary>
public static class BuilderStarter {
    /// <summary>
    /// Convert a non collection <see cref="IEnumerable{T}"/> into a countable version
    /// </summary>
    public static bool UseEnumerable<TBuilder, T>(this TBuilder builder, string variable, IEnumerable<T> value)
        where TBuilder : IQueryBuilder {
        if (value.TryGetNonEnumeratedCount(out var nb) || EnumerableCountProvider.TryGetNonEnumeratedCount(value, out nb)) {
            if (nb <= 0)
                return false;
            return builder.Use(variable, value);
        }
        var e = value.GetEnumerator();
        if (e.MoveNext())
            return builder.Use(variable, new PeekableWrapper(e.Current, e));
        (e as IDisposable)?.Dispose();
        return false;
    }
    /// <summary>
    /// Convert a non collection <see cref="IEnumerable{T}"/> into a countable version
    /// </summary>
    public static bool Use<TBuilder>(this TBuilder builder, string variable, IEnumerable value)
    where TBuilder : IQueryBuilder {
        if (value is IEnumerable<object> enu && enu.TryGetNonEnumeratedCount(out var nb)) {
            if (nb <= 0)
                return false;
            return builder.Use(variable, value);
        }
        if (value is ICollection col) {
            if (col.Count == 0)
                return false;
            return builder.Use(variable, col);
        }
        if (value.TryGetNonEnumeratedCount(out nb)) {
            if (nb <= 0)
                return false;
            return builder.Use(variable, value);
        }
        var e = value.GetEnumerator();
        if (e.MoveNext())
            return builder.Use(variable, new PeekableWrapper(e.Current, e));
        (e as IDisposable)?.Dispose();
        return false;
    }
    /// <summary>
    /// Start a <see cref="QueryBuilder"/>.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command)
        => new(command);
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/>.
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd)
        => new(command, cmd);
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/>.
    /// </summary>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd)
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
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd, params Span<(string, object)> values) {
        var builder = new QueryBuilderCommand<DbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/> and set usage with the <paramref name="values"/>
    /// </summary>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd, params Span<(string, object)> values) {
        var builder = new QueryBuilderCommand<IDbCommand>(command, cmd);
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
internal class PeekableWrapper(object? first, IEnumerator enumerator) : IEnumerable<object>, IDisposable {
    private object? _first = first;
    private IEnumerator? _enumerator = enumerator;

    public IEnumerator<object> GetEnumerator() {
        if (_enumerator == null)
            yield break;

        yield return _first!;
        _first = null;

        while (_enumerator.MoveNext())
            yield return _enumerator.Current;
        Dispose();
    }
    public void Dispose() {
        if (_enumerator is not null) {
            (_enumerator as IDisposable)?.Dispose();
            _enumerator = null;
            _first = null;
        }
        GC.SuppressFinalize(this);
    }
    ~PeekableWrapper() => Dispose();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}