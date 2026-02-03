using System.Data;
using System.Data.Common;

namespace RinkuLib.Queries;

public interface ICache {
    void UpdateCache(IDbCommand cmd);
}

public interface IParsingCache {
    public bool IsValid { get; }
}
public interface IParsingCache<T> : IParsingCache {
    public CommandBehavior Behavior { get; }
    public void Init(DbDataReader reader, IDbCommand cmd);
    public T Parse(DbDataReader reader);
}
public interface IParsingCacheAsync<T> {
    public CommandBehavior Behavior { get; }
    public Task Init(DbDataReader reader, IDbCommand cmd);
    public Task<T> Parse(DbDataReader reader);
}
public unsafe interface IParserCache<T> {
    public void Init(delegate*<DbDataReader, T> parser, CommandBehavior behavior);
}