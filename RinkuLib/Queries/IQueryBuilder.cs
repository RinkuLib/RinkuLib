using System.Data;
using System.Data.Common;

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
    /// Deactivates all items associated with the SELECT columns.
    /// </summary>
    void ResetSelects();
    /// <summary>
    /// Activates a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="condition">The name of the condition to activate.</param>
    void Use(string condition);
    /// <summary>
    /// Activates a condition that only functions as a toggle (such as a column or a conditional marker).
    /// </summary>
    /// <param name="condition">The name of the condition to activate.</param>
    void SafelyUse(string condition);
    /// <summary>
    /// Activates a variable and assigns it a data value.
    /// </summary>
    /// <param name="variable">The name of the item to activate.</param>
    /// <param name="value">The data value to assign.</param>
    /// <returns>True if the item is active after this call.</returns>
    /// <remarks>
    /// <b>Implementation Note:</b> While this method is intended for data-bearing variables, 
    /// passing a <see cref="bool"/> <c>true</c> can be used to activate a non-value condition. 
    /// However, passing <c>false</c> will simply result in a no-op (does not call <see cref="Remove"/>).
    /// </remarks>
    bool Use(string variable, object value);
}
public readonly unsafe struct ParsingCache<T>(/*delegate**/Func<DbDataReader, T> Parser, CommandBehavior Behavior) : IParsingCache<T> {
    public bool IsValid => parser != null;
    //public readonly delegate*<DbDataReader, T> parser = Parser;
    public readonly Func<DbDataReader, T> parser = Parser;
    public CommandBehavior Behavior { get; } = Behavior;
    public readonly void Init(DbDataReader reader, IDbCommand cmd) { }
    public T Parse(DbDataReader reader) => parser(reader);
}