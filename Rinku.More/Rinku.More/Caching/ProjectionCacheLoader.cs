using System.Collections;
using System.ComponentModel;

namespace Rinku.Caching;

public sealed class ProjectionCacheLoader<TSource, TResult, TContext>(CachedList<TSource, TContext> source, Func<IEnumerable<TSource>, IEnumerable<TResult>> project) : ICacheLoader<TResult, TContext>
{
    private ICacheCollection<TResult>? _target;
    private volatile bool _loaded;
    private bool _disposed;

    public bool IsLoaded => _loaded && source.IsLoaded;

    public void Invalidate()
    {
        _loaded = false;
        source.Invalidate();
    }

    internal void Observe(ICacheCollection<TResult> target)
    {
        lock (((ICollection)source).SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_target is null)
            {
                source.ListChanged += SourceChanged;
            }
            _target = target;
            Rebuild();
        }
    }

    public void Load(TContext context, ICacheCollection<TResult> target)
    {
        source.Load(context);
        Observe(target);
    }

    public async Task LoadAsync(TContext context, ICacheCollection<TResult> target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await source.LoadAsync(context, cancellationToken).ConfigureAwait(false);
        Observe(target);
    }

    private void SourceChanged(object? sender, ListChangedEventArgs args)
    {
        lock (((ICollection)source).SyncRoot)
        {
            if (!_disposed)
            {
                Rebuild();
            }
        }
    }

    private void Rebuild()
    {
        _target!.SetItems(project(source));
        _loaded = true;
    }

    public void Dispose()
    {
        lock (((ICollection)source).SyncRoot)
        {
            _disposed = true;
            source.ListChanged -= SourceChanged;
            _target = null;
        }
    }
}
