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

    public void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, T>? parsingFunc) {
        Cache1.UpdateCache(reader, cmd, ref parsingFunc);
        Cache2.UpdateCache(reader, cmd, ref parsingFunc);
    }

    public void UpdateCache(IDbCommand cmd) {
        Cache1.UpdateCache(cmd);
        Cache2.UpdateCache(cmd);
    }

    public void UpdateParser<T>(Func<DbDataReader, T> parser, CommandBehavior behavior) {
        Cache1.UpdateParser(parser, behavior);
        Cache2.UpdateParser(parser, behavior);
    }
}
public interface IParserCache : ICache {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, T>? parsingFunc);
    void UpdateParser<T>(Func<DbDataReader, T> parser, CommandBehavior behavior);
}
public interface IParserCacheParseAsync : ICache {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, Task<T>>? parsingFunc);
    void UpdateParser<T>(Func<DbDataReader, Task<T>> parser, CommandBehavior behavior);
}

public interface IParserCacheAsync : ICacheAsync {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    Task<Func<DbDataReader, T>?> UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, T>? parser);

    Task UpdateParser<T>(Func<DbDataReader, T> parser, CommandBehavior behavior);
}
public interface IParserCacheAsyncAndParseAsync : ICacheAsync {
    /// <summary> 
    /// Synchronizes the parser with the active execution context to perform metadata 
    /// updates, caching, or logic initialization. 
    /// </summary>
    Task<Func<DbDataReader, Task<T>>?> UpdateCache<T>(DbDataReader reader, IDbCommand cmd, Func<DbDataReader, Task<T>>? parser);
    Task UpdateParser<T>(Func<DbDataReader, Task<T>> parser, CommandBehavior behavior);
}