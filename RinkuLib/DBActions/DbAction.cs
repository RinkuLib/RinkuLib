using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.DBActions;

/// <inheritdoc/>
public delegate TItem Getter<TObj, TItem>(ref TObj instance);
/// <inheritdoc/>
public delegate void Setter<TObj, TItem>(ref TObj instance, TItem value);
/// <summary>
/// 
/// </summary>
public interface IParserGetter {
    /// <summary></summary>
    public DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser);
    /// <summary></summary>
    public Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default);
}
/// <summary>
/// 
/// </summary>
public abstract class DbAction<TParent> {
    /// <summary>
    /// 
    /// </summary>
    public abstract void Handle<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter)
        where TAccess : notnull, ICollectionRefAccessor<TParent>
        where TParserGetter : IParserGetter;
    /// <summary>
    /// 
    /// </summary>
    public abstract void Handle<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter) where TParserGetter : IParserGetter;
    /// <summary>
    /// 
    /// </summary>
    public abstract void Handle<TParserGetter>(ref TParent parent, TParserGetter parserGetter) where TParserGetter : IParserGetter;
    /// <summary>
    /// 
    /// </summary>
    public abstract ValueTask HandleAsync<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter, CancellationToken ct = default)
        where TAccess : notnull, ICollectionRefAccessor<TParent>
        where TParserGetter : IParserGetter;
    /// <summary>
    /// 
    /// </summary>
    public abstract ValueTask HandleAsync<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter;
    /// <summary>
    /// 
    /// </summary>
    public abstract ValueTask HandleAsync<TParserGetter>(TParent parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter;
}