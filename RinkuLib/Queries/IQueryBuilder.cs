using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace RinkuLib.Queries;

/// <summary>
/// The control interface for deciding which parts of a query are active and what data they carry.
/// </summary>
/// <remarks>
/// This interface treats a query as a set of optional pieces. By default, pieces are not used. 
/// You "activate" them by calling the <c>Use</c> methods. 
/// </remarks>
public interface IQueryBuilder {
    /// <summary>
    /// Returns the current state or value at the specified index.
    /// </summary>
    object? this[int ind] { get; }
    /// <summary>
    /// Returns the current state or value for a specific condition or variable name.
    /// </summary>
    object? this[string condition] { get; }
    /// <summary>
    /// Generates the SQL text only using the actual variables states without making or maintaining a command.
    /// </summary>
    string GetQueryText();
    /// <summary>
    /// Returns the position of an active item relative to other active items in the query.
    /// </summary>
    /// <param name="key">The name of the condition or variable.</param>
    /// <returns>The number of active items appearing before this one.</returns>
    int GetRelativeIndex(string key);
    /// <summary>
    /// Deactivates a condition or variable so it is no longer included in the query.
    /// </summary>
    void Remove(string condition);
    /// <summary>
    /// Deactivates all items, resetting the state of the query.
    /// </summary>
    void Reset();
    /// <summary>
    /// Activates a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="condition">The name of the condition to activate.</param>
    void Use(string condition);
    /// <summary>
    /// Activates a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="conditionIndex">The index of the condition to activate.</param>
    void Use(int conditionIndex);
    /// <summary>
    /// Desactivate a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="condition">The name of the condition to activate.</param>
    void UnUse(string condition);
    /// <summary>
    /// Desactivate a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="conditionIndex">The index of the condition to activate.</param>
    void UnUse(int conditionIndex);
    /// <summary>
    /// Activates a variable and assigns it a data value.
    /// </summary>
    /// <param name="variable">The name of the item to activate.</param>
    /// <param name="value">The data value to assign.</param>
    /// <returns>True if the item is active after this call.</returns>
    bool Use(string variable, object? value);
    /// <summary>
    /// Activates a variable and assigns it a data value.
    /// </summary>
    /// <param name="variableIndex">The index of the item to activate.</param>
    /// <param name="value">The data value to assign.</param>
    /// <returns>True if the item is active after this call.</returns>
    bool Use(int variableIndex, object? value);
    /// <summary>
    /// Set the builder state alligned with the parameter object
    /// </summary>
    public unsafe void UseWith(object parameterObj);
    /// <summary>
    /// Set the builder state alligned with the parameter object
    /// </summary>
    public unsafe void UseWith<T>(T parameterObj) where T : notnull;
    /// <summary>
    /// Set the builder state alligned with the parameter object
    /// </summary>
    public unsafe void UseWith<T>(ref T parameterObj) where T : notnull;
}
/// <summary>
/// A <see cref="ISchemaParser{T}"/> allready initialized
/// </summary>
public readonly unsafe struct SchemaParser<T>(/*delegate**/Func<DbDataReader, T> Parser, CommandBehavior Behavior) : ISchemaParser<T> {
    /// <inheritdoc/>
    public bool IsInit => parser != null;
    //public readonly delegate*<DbDataReader, T> parser = Parser;
    /// <summary>The actual function that do the parsing</summary>
    public readonly Func<DbDataReader, T> parser = Parser;
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; } = Behavior;
    /// <inheritdoc/>
    public readonly void Init(DbDataReader reader, IDbCommand cmd) { }
    /// <inheritdoc/>
    public T Parse(DbDataReader reader) => parser(reader);
}
/// <summary>
/// A <see cref="ISchemaParser{T}"/> allready initialized
/// </summary>
public readonly unsafe struct SchemaParserAsync<T>(/*delegate**/Func<DbDataReader, Task<T>> Parser, CommandBehavior Behavior) : ISchemaParserAsync<T> {
    /// <inheritdoc/>
    public bool IsInit => parser != null;
    //public readonly delegate*<DbDataReader, T> parser = Parser;
    /// <summary>The actual function that do the parsing</summary>
    public readonly Func<DbDataReader, Task<T>> parser = Parser;
    /// <inheritdoc/>
    public CommandBehavior Behavior { get; } = Behavior;
    /// <inheritdoc/>
    public readonly Task Init(DbDataReader reader, IDbCommand cmd) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task<T> Parse(DbDataReader reader) => parser(reader);
}