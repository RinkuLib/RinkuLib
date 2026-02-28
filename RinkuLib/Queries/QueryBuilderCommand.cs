using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.Queries;

/// <summary>
/// A stateful builder that actively synchronizes query state with a live database command.
/// </summary>
/// <remarks>
/// This struct manages the "active" state of a query. Unlike a standard builder, 
/// every call to <see cref="Use(string, object)"/> or <see cref="Remove(string)"/> immediately updates 
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
    public readonly bool Use(char charVariable, string variable, object? value) {
        Span<char> span = stackalloc char[variable.Length + 1];
        span[0] = charVariable;
        variable.AsSpan().CopyTo(span[1..]);
        return Use(QueryCommand.Mapper.GetIndex(span), value);
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
    /// </list>
    /// </remarks>
    public readonly bool Use(string variable, object? value)
        => Use(QueryCommand.Mapper.GetIndex(variable), value);
    /// <inheritdoc/>
    public readonly bool Use(ReadOnlySpan<char> variable, object? value)
        => Use(QueryCommand.Mapper.GetIndex(variable), value);
    /// <inheritdoc/>
    public readonly bool Use(int variableIndex, object? value) {
        if (variableIndex < 0 || variableIndex >= QueryCommand.StartBoolCond)
            return false;
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
            Variables[i] = accessor.IsUsed(i) ? QueryBuilder.Used : null;
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
            Variables[i] = accessor.IsUsed(i) ? QueryBuilder.Used : null;
    }
#endif
}
