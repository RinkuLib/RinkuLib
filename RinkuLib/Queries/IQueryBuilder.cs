namespace RinkuLib.Queries;

/// <summary>
/// Supplies a query's values from code instead of a parameter object. You set variables and switch
/// conditional parts on or off, then run the command off the builder. This is the road to take when the
/// values come from branching C# logic rather than a ready-made object.
/// </summary>
/// <remarks>
/// A query is a set of optional pieces. Nothing is included until you ask for it. A variable is a piece
/// that carries a value (a <c>@name</c> parameter). A condition is a piece that only turns on or off (a
/// conditional marker or a projected column), it carries no data. Both are addressed by name or, once you
/// know it, by index.
/// </remarks>
public interface IQueryBuilder {
    /// <summary>
    /// The value set for the variable or condition at <paramref name="ind"/>, or <see langword="null"/>
    /// when it is off.
    /// </summary>
    object? this[int ind] { get; }
    /// <summary>
    /// The value set for the named variable or condition, or <see langword="null"/> when it is off.
    /// </summary>
    object? this[string condition] { get; }
    /// <inheritdoc cref="this[string]"/>
    object? this[ReadOnlySpan<char> condition] { get; }
    /// <summary>
    /// The SQL text for the current state, with the off pieces dropped. Reads the state alone and touches
    /// no command, so you can inspect what a run would send without executing it.
    /// </summary>
    string GetQueryText();
    /// <summary>
    /// Turns off the named variable or condition so its part drops from the query.
    /// </summary>
    /// <param name="condition">The variable or condition name.</param>
    void Remove(string condition);
    /// <inheritdoc cref="Remove(string)"/>
    void Remove(ReadOnlySpan<char> condition);
    /// <summary>
    /// Turns everything off, back to the state of a fresh builder.
    /// </summary>
    void Reset();
    /// <summary>
    /// Turns on a condition, a piece that carries no value (a conditional marker or a projected column).
    /// </summary>
    /// <param name="condition">The condition name.</param>
    /// <returns><see langword="true"/> if the name is a condition and was turned on, <see langword="false"/> if it names a value-carrying variable instead.</returns>
    bool Use(string condition);
    /// <inheritdoc cref="Use(string)"/>
    bool Use(ReadOnlySpan<char> condition);
    /// <summary>
    /// Turns on the condition at <paramref name="conditionIndex"/>.
    /// </summary>
    /// <param name="conditionIndex">The condition index.</param>
    void Use(int conditionIndex);
    /// <summary>
    /// Turns off a condition previously turned on with <see cref="Use(string)"/>.
    /// </summary>
    /// <param name="condition">The condition name.</param>
    /// <returns><see langword="true"/> if the name is a condition, <see langword="false"/> if it names a value-carrying variable instead.</returns>
    bool UnUse(string condition);
    /// <inheritdoc cref="UnUse(string)"/>
    bool UnUse(ReadOnlySpan<char> condition);
    /// <summary>
    /// Turns off the condition at <paramref name="conditionIndex"/>.
    /// </summary>
    /// <param name="conditionIndex">The condition index.</param>
    void UnUse(int conditionIndex);

    /// <summary>
    /// Sets a variable to <paramref name="value"/> and turns it on, spelling the variable character apart
    /// from the name so the name can come from <c>nameof</c>.
    /// </summary>
    /// <param name="charVariable">The variable character the query uses, such as <c>@</c>.</param>
    /// <param name="variable">The variable name, without the leading character.</param>
    /// <param name="value">The value to bind.</param>
    /// <returns><see langword="true"/> if the name is a value-carrying variable and was set.</returns>
    public bool Use(char charVariable, string variable, object? value);
    /// <summary>
    /// Sets a variable to <paramref name="value"/> and turns it on. A <see langword="null"/> value turns it off.
    /// </summary>
    /// <param name="variable">The variable name.</param>
    /// <param name="value">The value to bind.</param>
    /// <returns><see langword="true"/> if the name is a value-carrying variable and was set.</returns>
    bool Use(string variable, object? value);
    /// <inheritdoc cref="Use(string, object)"/>
    bool Use(ReadOnlySpan<char> variable, object? value);
    /// <summary>
    /// Sets the variable at <paramref name="variableIndex"/> to <paramref name="value"/>.
    /// </summary>
    /// <param name="variableIndex">The variable index.</param>
    /// <param name="value">The value to bind.</param>
    void Use(int variableIndex, object? value);
    /// <summary>
    /// Sets every variable and condition at once from <paramref name="parameterObj"/>, matching its members
    /// to keys by name. The same object you would pass straight to a run, applied to the builder instead.
    /// </summary>
    public void UseWith(object parameterObj);
    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(T parameterObj) where T : notnull;
    /// <inheritdoc cref="UseWith(object)"/>
    public void UseWith<T>(ref T parameterObj) where T : notnull;
}