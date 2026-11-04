using Rinku.Caching;

namespace Rinku.More.Tests;

public class CacheCompositionTests
{
    [Fact]
    public async Task A_non_database_loader_uses_the_same_cache_and_owns_its_loading_state()
    {
        var loader = new IntegerLoader();
        using var cache = CachedList.New(loader);
        Assert.Same(loader, cache.Loader);
        cache.SetItems([99]);
        Assert.False(cache.IsLoaded);
        cache.Load(3);
        Assert.True(cache.IsLoaded);
        Assert.Equal([3], cache);
        await cache.LoadAsync(4);
        Assert.Equal([3], cache);
        await cache.ReloadAsync(4);
        Assert.Equal([4], cache);
        cache.Invalidate();
        Assert.False(loader.IsLoaded);
        await cache.LoadAsync(5);
        Assert.Equal([5], cache);
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(1, loader.DisposeCalls);
    }

    [Fact]
    public void Update_policies_can_be_composed_independently_of_any_loader()
    {
        using var target = CachedList.New<(int Id, string Name)>();
        ICacheUpdate<(int Id, string Name)> replace = new ReplaceCacheItems<(int Id, string Name)>();
        ICacheUpdate<(int Id, string Name)> upsert = new UpsertCacheItems<(int Id, string Name), int>(row => row.Id);
        replace.Apply(target, [(1, "A"), (2, "B")], full: true);
        upsert.Apply(target, [(1, "Changed"), (3, "C")], full: false);
        Assert.Equal([(1, "Changed"), (2, "B"), (3, "C")], target);
        replace.Apply(target, [(4, "D")], full: false);
        Assert.Equal([(4, "D")], target);
    }

    private class IntegerLoader : ICacheLoader<int, int>
    {
        public bool IsLoaded { get; private set; }
        public int DisposeCalls { get; private set; }
        public void Invalidate() => IsLoaded = false;
        public void Load(int context, ICacheCollection<int> target)
        {
            target.SetItems([context]);
            IsLoaded = true;
        }
        public Task LoadAsync(int context, ICacheCollection<int> target, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Load(context, target);
            return Task.CompletedTask;
        }
        public void Dispose() => DisposeCalls++;
    }
}
