using System.Runtime.CompilerServices;
using Rinku.Querying;
using Rinku.Mapping.Parsers;

namespace Rinku;
/// <summary>
/// Holds the values for one run of a <see cref="QueryCommand"/>. Use it when you want to set values and
/// conditions one at a time before running the query.
/// </summary>
/// <remarks>
/// Use <see cref="QueryBuilderCommand{TCommand}"/> when each change should also update one live
/// <see cref="System.Data.IDbCommand"/> for repeated execution.
/// </remarks>
public readonly struct QueryBuilder(QueryCommand QueryCommand) : IQueryBuilder {
    /// <summary>
    /// The value stored for a condition that is on but carries no data. Pass it where a value is expected
    /// to mean "present" for a toggle-only piece.
    /// </summary>
    public static readonly object Used = AccessorUsageMarker.Value;
    /// <summary> The command these values run against. </summary>
    public readonly QueryCommand QueryCommand = QueryCommand;
    /// <summary>
    /// The values for this run, one slot per key in the command. A slot is <see langword="null"/> when its
    /// piece is off, a bound value when a variable is set, and <see cref="Used"/> when a toggle-only
    /// condition is on.
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
    public readonly bool Use(char charVariable, string variable, object? value) 
        => Use(QueryCommand.Mapper.GetIndex(charVariable, variable), value);
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
        if (value is not null
            && variableIndex >= QueryCommand.StartSpecialHandlers && variableIndex < QueryCommand.StartBaseHandlers
            && !QueryCommand.Parameters._specialHandlers[variableIndex - QueryCommand.StartSpecialHandlers].CanHandle(ref value))
            value = null;
        Variables[variableIndex] = value;
        return true;
    }
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

    /// <summary>Copies the usable values from an object into this builder.</summary>
    public void UseWith(object parameterObj) {
        Type type = parameterObj.GetType();
        var accessor = QueryCommand.GetUseWithAccessor(type.TypeHandle.Value, type);
        accessor.Invoke(parameterObj, Variables);
    }

    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(T parameterObj) where T : notnull {
        if (!typeof(T).IsValueType) {
            var accessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
            accessor.Invoke(parameterObj, Variables);
            return;
        }
        var valueAccessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
        var typed = Unsafe.As<UseWithAccessor, UseWithAccessor<T>>(ref valueAccessor);
        typed.InvokeTyped(ref parameterObj, Variables);
    }

    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(ref T parameterObj) where T : notnull {
        if (!typeof(T).IsValueType) {
            var accessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
            accessor.Invoke(parameterObj, Variables);
            return;
        }
        var valueAccessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
        var typed = Unsafe.As<UseWithAccessor, UseWithAccessor<T>>(ref valueAccessor);
        typed.InvokeTyped(ref parameterObj, Variables);
    }
}
