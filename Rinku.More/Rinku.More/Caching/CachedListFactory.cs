using System.ComponentModel;
using System.Data.Common;

namespace Rinku.Caching;

public static class CachedList
{
    public static CachedList<T, object?> New<T>() => new();

    public static CachedList<T, TContext> New<T, TContext>(ICacheLoader<T, TContext> loader) => new(loader);

    public static CachedList<TResult, TContext> Project<TSource, TResult, TContext>(CachedList<TSource, TContext> source, Func<IEnumerable<TSource>, IEnumerable<TResult>> project)
    {
        var loader = new ProjectionCacheLoader<TSource, TResult, TContext>(source, project);
        var cache = New(loader);
        try
        {
            loader.Observe(cache);
            return cache;
        }
        catch
        {
            cache.Dispose();
            throw;
        }
    }

    public static CachedList<TResult, TContext> Project<TSource, TResult, TContext>(CachedList<TSource, TContext> source, Func<TSource, TResult> select, IEqualityComparer<TResult> comparer) => Project<TSource, TResult, TContext>(source, items => items.Select(select).Distinct(comparer));

    public static CachedList<T, DbConnection> New<T>(Func<DbConnection, DbCommand> command, ICacheUpdate<T>? update = null) => new(new CommandCacheLoader<T>(command, update));

    public static CachedList<T, DbConnection> New<T>(Func<DbConnection, DateTime?, DbCommand> command, ICacheUpdate<T>? update = null) => new(new CommandCacheLoader<T>(command, update));

    public static CachedList<T, DbConnection> New<T, TKey>(Func<DbConnection, DateTime?, DbCommand> command, Func<T, TKey> key, ListSortDirection? order = null) => New(command, new UpsertCacheItems<T, TKey>(key, order));
}
