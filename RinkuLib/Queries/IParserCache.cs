using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;
/// <summary>
/// Contract to update internal cache
/// </summary>
public interface ICache {
    /// <summary>
    /// Uses a <see cref="IDbCommand"/> that has just been executed (<see cref="IDbCommand.ExecuteNonQuery"/>, 
    /// <see cref="IDbCommand.ExecuteReader()"/>, ...) to update the internal cache
    /// </summary>
    void UpdateCache(IDbCommand cmd);
}

/// <summary>
/// Base contract a parsing function (schema to object)
/// </summary>
public interface ISchemaParser {
    /// <summary>Indicate if the parser actualy needs to be initialized or if it allready is</summary>
    public bool IsInit { get; }
}
/// <summary>
/// Interface that represent a specific way to parse a schema
/// </summary>
public interface ISchemaParser<T> : ISchemaParser {
    /// <summary>Indicate the default <see cref="CommandBehavior"/> that can be use to call <see cref="IDbCommand.ExecuteReader(CommandBehavior)"/></summary>
    /// <remarks>May be something like <see cref="CommandBehavior.SequentialAccess"/> or <see cref="CommandBehavior.SingleResult"/></remarks>
    public CommandBehavior Behavior { get; }
    /// <summary>Initialization before using the <see cref="DbDataReader"/></summary>
    public void Init(DbDataReader reader, IDbCommand cmd);
    /// <summary>Parsing of the <see cref="DbDataReader"/></summary>
    public T Parse(DbDataReader reader);
}
/// <summary>
/// Interface that represent a specific way to parse a schema asyncly
/// </summary>
public interface ISchemaParserAsync<T> {
    /// <summary>Indicate the default <see cref="CommandBehavior"/> that can be use to call <see cref="IDbCommand.ExecuteReader(CommandBehavior)"/></summary>
    /// <remarks>May be something like <see cref="CommandBehavior.SequentialAccess"/> or <see cref="CommandBehavior.SingleResult"/></remarks>
    public CommandBehavior Behavior { get; }
    /// <summary>Initialization before using the <see cref="DbDataReader"/></summary>
    public Task Init(DbDataReader reader, IDbCommand cmd);
    /// <summary>Parsing of the <see cref="DbDataReader"/></summary>
    public Task<T> Parse(DbDataReader reader);
}