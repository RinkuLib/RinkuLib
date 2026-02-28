using System.Collections;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;

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
    /// <item><b>Data Items (Variables/Handlers):</b> 
    /// Indices 0 to <see cref="QueryCommand.StartBoolCond"/> - 1. 
    /// These require a value to be functional.</item>
    /// <item><b>Binary Items (Comment conditions):</b> 
    /// Indices <see cref="QueryCommand.StartBoolCond"/> to Count - 1. 
    /// These signify presence only and carry no data.</item>
    /// </list>
    /// </summary>
    public readonly object?[] Variables = new object?[QueryCommand.Mapper.Count];
    /// <inheritdoc/>
    public readonly void Reset()
        => Array.Clear(Variables, 0, Variables.Length);
    /// <inheritdoc/>
    public readonly void Remove(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        Variables[ind] = null;
    }
    /// <inheritdoc/>
    public readonly void Remove(ReadOnlySpan<char> condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        Variables[ind] = null;
    }
    /// <inheritdoc/>
    public readonly bool Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = Used;
        return true;
    }
    /// <inheritdoc/>
    public readonly bool Use(ReadOnlySpan<char> condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = Used;
        return true;
    }
    /// <inheritdoc/>
    public void Use(int conditionIndex)
        => Variables[conditionIndex] = Used;

    /// <inheritdoc/>
    public bool UnUse(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = null;
        return true;
    }
    /// <inheritdoc/>
    public bool UnUse(ReadOnlySpan<char> condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = null;
        return true;
    }

    /// <inheritdoc/>
    public void UnUse(int conditionIndex)
        => Variables[conditionIndex] = null;
    /// <inheritdoc/>
    public readonly bool Use(char charVariable, string variable, object? value) {
        Span<char> span = stackalloc char[variable.Length + 1];
        span[0] = charVariable;
        variable.AsSpan().CopyTo(span[1..]);
        return Use(QueryCommand.Mapper.GetIndex(span), value);
    }
    /// <inheritdoc/>
    public readonly bool Use(string variable, object? value)
        => Use(QueryCommand.Mapper.GetIndex(variable), value);
    /// <inheritdoc/>
    public readonly bool Use(ReadOnlySpan<char> variable, object? value)
        => Use(QueryCommand.Mapper.GetIndex(variable), value);
    /// <inheritdoc/>
    public bool Use(int variableIndex, object? value) {
        if (variableIndex < 0 || variableIndex >= QueryCommand.StartBoolCond)
            return false;
        if (variableIndex >= QueryCommand.StartSpecialHandlers && value is IEnumerable && value is not string && !HasAny(ref Unsafe.As<object, IEnumerable>(ref value)))
            return false;
        Variables[variableIndex] = value;
        return true;
    }
    /// <inheritdoc/>
    void IQueryBuilder.Use(int variableIndex, object? value) => Use(variableIndex, value);
    /// <inheritdoc/>
    public readonly object? this[string condition] {
        get => Variables[QueryCommand.Mapper.GetIndex(condition)];
    }
    /// <inheritdoc/>
    public readonly object? this[ReadOnlySpan<char> condition] {
        get => Variables[QueryCommand.Mapper.GetIndex(condition)];
    }
    /// <inheritdoc/>
    public readonly object? this[int ind] {
        get => Variables[ind];
    }
    /// <inheritdoc/>
    public readonly string GetQueryText()
        => QueryCommand.QueryText.Parse(Variables);
    internal static bool HasAny(ref IEnumerable value) {
        if (value is not IEnumerable source)
            return true;
        if (source is IEnumerable<object> enu && enu.TryGetNonEnumeratedCount(out var nb)) {
            if (nb <= 0)
                return false;
            return true;
        }
        if (source is ICollection col) {
            if (col.Count <= 0)
                return false;
            return true;
        }
        if (source.TryGetNonEnumeratedCount(out nb)) {
            if (nb <= 0)
                return false;
            return true;
        }
        var e = source.GetEnumerator();
        if (e.MoveNext()) {
            value = new PeekableWrapper(e.Current, e);
            return true;
        }
        (e as IDisposable)?.Dispose();
        return false;
    }
    /// <inheritdoc/>
    public void UseWith(object parameterObj) {
        Type type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        var cache = QueryCommand.GetAccessorCache(handle, type);
        UpdateCommand(new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue));
    }
    /// <inheritdoc/>
    public void UseWith<T>(T parameterObj) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = QueryCommand.GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType) {
            UpdateCommand(new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue));
            return;
        }
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        UpdateCommand(new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue));
    }
    /// <inheritdoc/>
    public void UseWith<T>(ref T parameterObj) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = QueryCommand.GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType) {
            UpdateCommand(new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue));
            return;
        }
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        UpdateCommand(new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue));
    }
#if NET9_0_OR_GREATER
    private void UpdateCommand<T>(T accessor) where T : ITypeAccessor, allows ref struct
#else
    private void UpdateCommand(TypeAccessor accessor)
#endif
    {
        var mapper = QueryCommand.Mapper;
        var endVariables = QueryCommand.StartBoolCond;
        var total = mapper.Count;
        int i = 0;
        for (; i < endVariables; i++)
            Use(i, accessor.IsUsed(i) ? accessor.GetValue(i) : null);
        for (; i < total; i++)
            Variables[i] = accessor.IsUsed(i) ? Used : null;
    }
#if !NET9_0_OR_GREATER
    private void UpdateCommand<T>(TypeAccessor<T> accessor) {
        var mapper = QueryCommand.Mapper;
        var endVariables = QueryCommand.StartBoolCond;
        var total = mapper.Count;
        int i = 0;
        for (; i < endVariables; i++)
            Use(i, accessor.IsUsed(i) ? accessor.GetValue(i) : null);
        for (; i < total; i++)
            Variables[i] = accessor.IsUsed(i) ? Used : null;
    }
#endif
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
