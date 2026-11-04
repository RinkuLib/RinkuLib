using System.Data.Common;

namespace Rinku.Caching;

public class CommandCacheLoader<T> : ICacheLoader<T, DbConnection>
{
    private readonly Func<DbConnection, DateTime?, DbCommand> _command;
    private readonly ICacheUpdate<T> _update;
    private readonly CachedTypeParser<IEnumerable<T>> _parser = new();
    private readonly object _gate = new();
    private DateTime? _lastLoadedUtc;
    private int _generation;

    public CommandCacheLoader(Func<DbConnection, DbCommand> command, ICacheUpdate<T>? update = null) : this((connection, _) => command(connection), update) { }

    public CommandCacheLoader(Func<DbConnection, DateTime?, DbCommand> command, ICacheUpdate<T>? update = null)
    {
        _command = command;
        _update = update ?? new ReplaceCacheItems<T>();
    }

    public DateTime? LastLoadedUtc
    {
        get
        {
            lock (_gate)
            {
                return _lastLoadedUtc;
            }
        }
    }

    public bool IsLoaded => LastLoadedUtc.HasValue;

    public void Invalidate()
    {
        lock (_gate)
        {
            _lastLoadedUtc = null;
            _generation++;
        }
    }

    public void Load(DbConnection context, ICacheCollection<T> target)
    {
        var read = BeginRead();
        using var command = _command(context, read.Since);
        Apply(target, _parser.Query(command), read);
    }

    public async Task LoadAsync(DbConnection context, ICacheCollection<T> target, CancellationToken cancellationToken = default)
    {
        var read = BeginRead();
        await using var command = _command(context, read.Since);
        Apply(target, await _parser.QueryAsync(command, ct: cancellationToken).ConfigureAwait(false), read);
    }

    private (DateTime? Since, DateTime StartedUtc, int Generation) BeginRead()
    {
        lock (_gate)
        {
            return (_lastLoadedUtc, DateTime.UtcNow, _generation);
        }
    }

    private void Apply(ICacheCollection<T> target, IEnumerable<T> items, (DateTime? Since, DateTime StartedUtc, int Generation) read)
    {
        _update.Apply(target, items, read.Since is null);
        lock (_gate)
        {
            if (_generation == read.Generation)
            {
                _lastLoadedUtc = read.StartedUtc;
            }
        }
    }

    public void Dispose() => _parser.Dispose();
}
