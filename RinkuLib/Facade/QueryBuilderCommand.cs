using System.Buffers;
using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinku.Querying;
using Rinku.Mapping.Parsers;

namespace Rinku;
/// <summary>
/// Holds the values for a run and keeps a live <see cref="System.Data.IDbCommand"/> in step with them. Each
/// time you set a variable the matching command parameter is added, updated, or dropped on the spot, so the
/// command is always ready to run. Bind one command and a loop reuses it across a batch without rebuilding
/// it each pass.
/// </summary>
/// <remarks>
/// This is the live-command counterpart to <see cref="QueryBuilder"/>, which keeps its values in memory and
/// builds a command only when it runs.
/// </remarks>
public readonly struct QueryBuilderCommand<TCommand>(QueryCommand QueryCommand, TCommand Command) : IQueryBuilder where TCommand : IDbCommand {
    /// <summary> The command these values run against. </summary>
    public readonly QueryCommand QueryCommand = QueryCommand;
    /// <summary>
    /// The values for this run, one slot per key. Setting a slot also updates the matching parameter on
    /// <see cref="Command"/>.
    /// </summary>
    public readonly object?[] Variables = new object?[QueryCommand.Mapper.Count];
    /// <summary> The live command kept in step with the values. </summary>
    public readonly TCommand Command = Command;
    /// <inheritdoc/>
    public readonly void Reset() {
        var varInfos = QueryCommand.Parameters._variablesInfo;
        ref object? pVar = ref MemoryMarshal.GetReference(Variables);
        for (int i = 0; i < varInfos.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pVar, i);
            if (currentVar is not null) {
                varInfos[i].Remove(Command, currentVar);
                currentVar = null;
            }
        }
        var handlers = QueryCommand.Parameters._specialHandlers;
        ref object? pSpecialVar = ref Unsafe.Add(ref MemoryMarshal.GetReference(Variables), varInfos.Length);
        for (int i = 0; i < handlers.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is not null) {
                handlers[i].Update(Command, ref currentVar, null);
                currentVar = null;
            }
        }
    }
    /// <inheritdoc/>
    public readonly void Remove(int ind) {
        if (ind < 0)
            return;
        if (ind >= QueryCommand.StartBaseHandlers) {
            Variables[ind] = null;
            return;
        }
        ref var val = ref Variables[ind];
        if (val is null)
            return;
        if (ind < QueryCommand.StartSpecialHandlers) {
            QueryCommand.Parameters._variablesInfo[ind].Remove(Command, val);
            val = null;
        }
        else if (ind < QueryCommand.StartBaseHandlers)
            QueryCommand.Parameters._specialHandlers[ind - QueryCommand.StartSpecialHandlers].Update(Command, ref val, null);
    }
    /// <inheritdoc/>
    public readonly void Remove(string condition) 
        => Remove(QueryCommand.Mapper.GetIndex(condition));
    /// <inheritdoc/>
    public readonly void Remove(ReadOnlySpan<char> condition)
        => Remove(QueryCommand.Mapper.GetIndex(condition));
    /// <inheritdoc/>
    public readonly bool Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = QueryBuilder.Used;
        return true;
    }
    /// <inheritdoc/>
    public readonly bool Use(ReadOnlySpan<char> condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            return false;
        Variables[ind] = QueryBuilder.Used;
        return true;
    }
    /// <inheritdoc/>
    public readonly void Use(int conditionIndex) 
        => Variables[conditionIndex] = QueryBuilder.Used;
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
    public readonly bool Use(int variableIndex, object? value) {
        if (variableIndex < 0 || variableIndex >= QueryCommand.StartBoolCond)
            return false;
        if (value is not null
            && variableIndex >= QueryCommand.StartSpecialHandlers && variableIndex < QueryCommand.StartBaseHandlers
            && !QueryCommand.Parameters._specialHandlers[variableIndex - QueryCommand.StartSpecialHandlers].CanHandle(ref value))
            value = null;
        if (value is null) {
            ref var vall = ref Variables[variableIndex];
            if (vall is null)
                return true;
            if (variableIndex < QueryCommand.StartSpecialHandlers) {
                QueryCommand.Parameters._variablesInfo[variableIndex].Remove(Command, vall);
                vall = null;
            }
            else if (variableIndex < QueryCommand.StartBaseHandlers)
                QueryCommand.Parameters._specialHandlers[variableIndex - QueryCommand.StartSpecialHandlers].Update(Command, ref vall, null);
            return true;
        }
        ref var val = ref Variables[variableIndex];
        if (val is null) {
            bool res;
            if (variableIndex < QueryCommand.StartSpecialHandlers) {
                var key = QueryCommand.Mapper.GetKey(variableIndex);
                res = QueryCommand.Parameters._variablesInfo[variableIndex].SaveUse(key, Command, ref value);
            }
            else if (variableIndex < QueryCommand.StartBaseHandlers)
                res = QueryCommand.Parameters._specialHandlers[variableIndex - QueryCommand.StartSpecialHandlers].SaveUse(Command, ref value);
            else
                res = true;
            if (res)
                val = value;
            return res;
        }
        if (variableIndex < QueryCommand.StartSpecialHandlers)
            return QueryCommand.Parameters._variablesInfo[variableIndex].Update(Command, ref val, value);
        if (variableIndex < QueryCommand.StartBaseHandlers)
            return QueryCommand.Parameters._specialHandlers[variableIndex - QueryCommand.StartSpecialHandlers].Update(Command, ref val, value);
        val = value;
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

    /// <summary>
    /// Replaces the current values from an object and immediately synchronizes the live command.
    /// Special handlers process the raw values during synchronization.
    /// </summary>
    public void UseWith(object parameterObj) {
        Type type = parameterObj.GetType();
        var accessor = QueryCommand.GetUseWithAccessor(type.TypeHandle.Value, type);
        var values = RentUseWithValues();
        try {
            accessor.Invoke(parameterObj, values);
            ApplyUseWithValues(values);
        }
        finally { ReturnUseWithValues(values); }
    }

    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(T parameterObj) where T : notnull {
        var accessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
        var values = RentUseWithValues();
        try {
            if (!typeof(T).IsValueType)
                accessor.Invoke(parameterObj!, values);
            else {
                var typed = Unsafe.As<UseWithAccessor, UseWithAccessor<T>>(ref accessor);
                typed.InvokeTyped(ref parameterObj, values);
            }
            ApplyUseWithValues(values);
        }
        finally { ReturnUseWithValues(values); }
    }

    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(ref T parameterObj) where T : notnull {
        var accessor = QueryCommand.GetUseWithAccessor(typeof(T).TypeHandle.Value, typeof(T));
        var values = RentUseWithValues();
        try {
            if (!typeof(T).IsValueType)
                accessor.Invoke(parameterObj!, values);
            else {
                var typed = Unsafe.As<UseWithAccessor, UseWithAccessor<T>>(ref accessor);
                typed.InvokeTyped(ref parameterObj, values);
            }
            ApplyUseWithValues(values);
        }
        finally { ReturnUseWithValues(values); }
    }

    private readonly object?[] RentUseWithValues() {
        var values = ArrayPool<object?>.Shared.Rent(Variables.Length);
        Array.Clear(values, 0, Variables.Length);
        return values;
    }

    private readonly void ApplyUseWithValues(object?[] values) {
        for (int i = 0; i < Variables.Length; i++)
            Use(i, values[i]);
        QueryCommand.SetText(Command, QueryCommand.QueryText.Parse(Variables));
    }

    private readonly void ReturnUseWithValues(object?[] values) {
        Array.Clear(values, 0, Variables.Length);
        ArrayPool<object?>.Shared.Return(values);
    }

}
