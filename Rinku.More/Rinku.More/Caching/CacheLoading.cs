namespace Rinku.Caching;

public interface ICacheCollection<T> : IList<T>
{
    void SetItems(IEnumerable<T> items);
}

public interface ICacheLoader<T, in TContext> : IDisposable
{
    bool IsLoaded { get; }
    void Invalidate();
    void Load(TContext context, ICacheCollection<T> target);
    Task LoadAsync(TContext context, ICacheCollection<T> target, CancellationToken cancellationToken = default);
}

public interface IReloadableCache<in TContext> : ILazyLoaded<TContext>
{
    void Reload(TContext context);
    Task ReloadAsync(TContext context, CancellationToken cancellationToken = default);
    void InvalidateAndLoad(TContext context);
    Task InvalidateAndLoadAsync(TContext context, CancellationToken cancellationToken = default);
    Task WaitForLoadAsync(CancellationToken cancellationToken = default);
}

public sealed partial class CachedList<T, TContext>
{
    public void InvalidateAndLoad(TContext context)
    {
        Invalidate();
        Load(context);
    }

    public Task InvalidateAndLoadAsync(TContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invalidate();
        return LoadAsync(context, cancellationToken);
    }

    public void Load(TContext context)
    {
        while (!IsLoaded || IsLoading)
        {
            ReloadCore(context);
        }
    }

    public async Task LoadAsync(TContext context, CancellationToken cancellationToken = default)
    {
        while (!IsLoaded || IsLoading)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReloadCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Reload(TContext context)
    {
        ReloadCore(context, force: true);
        Load(context);
    }

    public async Task ReloadAsync(TContext context, CancellationToken cancellationToken = default)
    {
        await ReloadCoreAsync(context, cancellationToken, force: true).ConfigureAwait(false);
        await LoadAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public Task WaitForLoadAsync(CancellationToken cancellationToken = default) => WaitForLoad(cancellationToken);

    void ICacheLoader<T, TContext>.Load(TContext context, ICacheCollection<T> target)
    {
        Load(context);
        lock (_sync)
        {
            target.SetItems(this);
        }
    }

    async Task ICacheLoader<T, TContext>.LoadAsync(TContext context, ICacheCollection<T> target, CancellationToken cancellationToken)
    {
        await LoadAsync(context, cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            target.SetItems(this);
        }
    }

    private async Task DisposeLoaderAsync()
    {
        try
        {
            await WaitForLoad(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            Loader?.Dispose();
        }
    }
}
