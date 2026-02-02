using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;

public interface ICache {
    void UpdateCache(IDbCommand cmd);
}

public interface ICacheAsync {
    Task UpdateCache(IDbCommand cmd);
}

internal class CacheWrapper(IParserCache Cache1, IParserCache Cache2) : IParserCache {
    private readonly IParserCache Cache1 = Cache1;
    private readonly IParserCache Cache2 = Cache2;

    public void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, T>? parsingFunc, CommandBehavior behavior) {
        Cache1.UpdateCache(reader, cmd, parsingFunc, behavior);
        Cache2.UpdateCache(reader, cmd, parsingFunc, behavior);
    }
}
public interface IParserCache {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, T>? parsingFunc, CommandBehavior behavior);
}
public interface IParserCacheParseAsync {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, Task<T>>? parsingFunc, CommandBehavior behavior);
}

public interface IParserCacheAsync {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    Task UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, T>? parser, CommandBehavior behavior);
}
public interface IParserCacheAsyncAndParseAsync {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    Task UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, Task<T>>? parser, CommandBehavior behavior);
}