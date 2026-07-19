using System.Runtime.CompilerServices;
using RinkuLib.Queries;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;
/// <summary>
/// Holds the values for one run of a <see cref="Queries.QueryCommand"/> in memory. You set variables and
/// switch conditions on the builder, then run the command off it. Because the values live here and not on
/// the command, the command stays stateless and shared while each builder is one call's worth of state.
/// </summary>
/// <remarks>
/// Reach for this form when the values come from C# logic rather than a ready-made object. To sync each
/// change onto a live command instead, so a loop can reuse one <see cref="System.Data.IDbCommand"/>, use
/// <see cref="QueryBuilderCommand{TCommand}"/>.
/// </remarks>
public readonly struct QueryBuilder(QueryCommand QueryCommand) : IQueryBuilder {
    /// <summary>
    /// The value stored for a condition that is on but carries no data. Pass it where a value is expected
    /// to mean "present" for a toggle-only piece.
    /// </summary>
    public static readonly object Used = new();
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