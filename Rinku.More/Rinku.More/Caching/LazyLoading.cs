using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Rinku.Caching;

public interface ILazyLoaded<in TContext> : ILazyLoaded
{
    void Load(TContext context);
    Task LoadAsync(TContext context, CancellationToken cancellationToken = default);
}

public static class DatabaseCacheExtensions
{
    public static TCache EnsureLoaded<TCache>(this IDatabase database, TCache cache, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(cache))] string cacheSource = "") where TCache : ILazyLoaded
    {
        if (cache is ILazyLoaded<DbConnection> loader && !loader.IsLoaded && !loader.IsLoading)
        {
            _ = Observe(database.ExecuteAsync(loader.LoadAsync, cancellationToken, cacheSource));
        }
        return cache;
    }

    private static async Task Observe(Task loading)
    {
        try
        {
            await loading.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
