using Rinku.Caching;

namespace Rinku.More.Tests;

internal sealed class TestCacheLoader<T>(Func<CancellationToken, Task<T[]>> read) : ICacheLoader<T, int>
{
    private int _generation;
    private int _loadedGeneration = -1;
    public bool IsLoaded => Volatile.Read(ref _loadedGeneration) == Volatile.Read(ref _generation);
    public void Invalidate() => Interlocked.Increment(ref _generation);
    public void Load(int context, ICacheCollection<T> target) => LoadAsync(context, target).GetAwaiter().GetResult();
    public async Task LoadAsync(int context, ICacheCollection<T> target, CancellationToken cancellationToken = default)
    {
        int generation = Volatile.Read(ref _generation);
        target.SetItems(await read(cancellationToken));
        Volatile.Write(ref _loadedGeneration, generation);
    }
    public void Dispose() { }
}
