namespace Rinku.More;

public interface IConsumableBuffer<T> : IDisposable, IAsyncDisposable
{
    int Count { get; }
    void Add(T item);
    Task<bool> ConsumeAsync(Func<IReadOnlyList<T>, CancellationToken, Task> consume, CancellationToken cancellationToken = default);
}

/// <summary>Buffers additions concurrently and hands a detached batch to its consumer. Failed batches remain pending for retry.</summary>
public class ConcurrentBuffer<T> : IConsumableBuffer<T>
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _consume = new(1, 1);
    private List<T> _pending = [];
    private List<T> _batch = [];
    private bool _closed;
    public event Action<Exception>? ConsumptionFailed;

    public int Count
    {
        get
        {
            lock (_gate)
                return _pending.Count + _batch.Count;
        }
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _pending.Add(item);
        }
    }

    public async Task<bool> ConsumeAsync(Func<IReadOnlyList<T>, CancellationToken, Task> consume, CancellationToken cancellationToken = default)
    {
        await _consume.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_pending.Count == 0)
                    return true;
                (_pending, _batch) = (_batch, _pending);
            }
            try
            {
                await consume(_batch, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                    _batch.Clear();
                return true;
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _batch.AddRange(_pending);
                    _pending.Clear();
                    (_pending, _batch) = (_batch, _pending);
                }
                if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;
                ConsumptionFailed?.Invoke(error);
                return false;
            }
        }
        finally
        {
            _consume.Release();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
            _closed = true;
        await _consume.WaitAsync().ConfigureAwait(false);
        _consume.Release();
    }
}
