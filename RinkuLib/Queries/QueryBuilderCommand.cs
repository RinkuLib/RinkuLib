using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// A stateful builder that actively synchronizes query state with a live database command.
/// </summary>
/// <remarks>
/// This struct manages the "active" state of a query. Unlike a standard builder, 
/// every call to <see cref="Use(string, object)"/> or <see cref="Remove"/> immediately updates 
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
    public readonly void Remove(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
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
    public readonly void Use(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            throw new ArgumentException(condition);
        Variables[ind] = QueryBuilder.Used;
    }
    /// <inheritdoc/>
    public readonly void Use(int conditionIndex) 
        => Variables[conditionIndex] = QueryBuilder.Used;
    /// <inheritdoc/>
    public void UnUse(string condition) {
        var ind = QueryCommand.Mapper.GetIndex(condition);
        if (ind < QueryCommand.StartBoolCond)
            throw new ArgumentException(condition);
        Variables[ind] = null;
    }

    /// <inheritdoc/>
    public void UnUse(int conditionIndex)
        => Variables[conditionIndex] = null;
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


    /// <inheritdoc/>
    public unsafe void UseWith(object parameterObj) {
        Type type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        if (type.IsValueType) {
            fixed (void* objPtr = &Unsafe.As<object, byte>(ref parameterObj)) {
                void* dataPtr = (*(byte**)objPtr) + IntPtr.Size;
                UpdateCommand(QueryCommand.GetAccessor(dataPtr, handle, type));
            }
            return;
        }
        fixed (void* ptr = &Unsafe.As<object, byte>(ref parameterObj)) {
            void* instancePtr = *(void**)ptr;
            UpdateCommand(QueryCommand.GetAccessor(instancePtr, handle, type));
        }
    }
    /// <inheritdoc/>
    public unsafe void UseWith<T>(T parameterObj) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;

        if (typeof(T).IsValueType) {
            UpdateCommand(QueryCommand.GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)));
            return;
        }
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            UpdateCommand(QueryCommand.GetAccessor(*(void**)ptr, handle, typeof(T)));
        }
    }
    /// <inheritdoc/>
    public unsafe void UseWith<T>(ref T parameterObj) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (typeof(T).IsValueType) {
            fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj))
                UpdateCommand(QueryCommand.GetAccessor(ptr, handle, typeof(T)));
            return;
        }
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            UpdateCommand(QueryCommand.GetAccessor(*(void**)ptr, handle, typeof(T)));
        }
    }
    private void UpdateCommand(TypeAccessor accessor) {
        var mapper = QueryCommand.Mapper;
        var endVariables = QueryCommand.StartBoolCond;
        var total = mapper.Count;
        int i = 0;
        for (; i < endVariables; i++)
            Use(i, accessor.IsUsed(i) ? accessor.GetValue(i) : null);
        for (; i < total; i++)
            Variables[i] = accessor.IsUsed(i) ? QueryBuilder.Used : null;
    }
}
