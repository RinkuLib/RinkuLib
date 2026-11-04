using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Rinku.Caching;

namespace Rinku.More.Tests;

public class CommandCacheLoaderTests
{
    public record Item(int Id, string Name);

    [Theory]
    [InlineData(ListSortDirection.Ascending)]
    [InlineData(ListSortDirection.Descending)]
    public async Task Ordered_deltas_update_matches_and_insert_at_the_beginning_middle_and_end(ListSortDirection order)
    {
        using var cache = CachedList.New<Item, int>((_, since) =>
        {
            Item[] rows = since is null ? [new(2, "B"), new(4, "D"), new(6, "F")] : [new(1, "A"), new(3, "C"), new(4, "Changed"), new(7, "G")];
            return new ReaderCommand(_ => Task.FromResult(Rows(order == ListSortDirection.Ascending ? rows.OrderBy(item => item.Id).ToArray() : rows.OrderByDescending(item => item.Id).ToArray())));
        }, item => item.Id, order);
        await cache.LoadAsync(null!);
        Item untouched = cache.Single(item => item.Id == 6);
        await cache.ReloadAsync(null!);
        int[] ascending = [1, 2, 3, 4, 6, 7];
        Assert.Equal(order == ListSortDirection.Ascending ? ascending : ascending.Reverse(), cache.Select(item => item.Id));
        Assert.Equal("Changed", cache.Single(item => item.Id == 4).Name);
        Assert.Same(untouched, cache.Single(item => item.Id == 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_load_replaces_then_refresh_upserts_and_an_empty_delta_keeps_existing_items(bool asynchronous)
    {
        List<DateTime?> timestamps = [];
        List<ReaderCommand> commands = [];
        int reads = 0;
        using var cache = CachedList.New<Item, int>((_, since) =>
        {
            timestamps.Add(since);
            var query = new ReaderCommand(_ => Task.FromResult(Rows(++reads switch
            {
                1 => [new(1, "A"), new(2, "B")],
                2 => [new(1, "Changed"), new(3, "C"), new(3, "Latest")],
                _ => []
            })));
            commands.Add(query);
            return query;
        }, item => item.Id);
        cache.Add(new(9, "Replaced"));
        List<ListChangedType> changes = [];
        cache.ListChanged += (_, args) => changes.Add(args.ListChangedType);

        await Load();
        DateTime? first = ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc;
        Assert.NotNull(first);
        Assert.Null(timestamps[0]);
        Assert.Equal(DateTimeKind.Utc, first.Value.Kind);
        Item untouched = cache[1];
        await Load();
        Assert.Equal(1, reads);
        changes.Clear();
        await Load(reload: true);
        Assert.Equal(first, timestamps[1]);
        Assert.Equal([new(1, "Changed"), new(2, "B"), new Item(3, "Latest")], cache);
        Assert.Same(untouched, cache[1]);
        Assert.Equal([ListChangedType.ItemChanged, ListChangedType.ItemAdded, ListChangedType.ItemChanged], changes);
        DateTime? second = ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc;
        Item changed = cache[0];
        changes.Clear();
        await Load(reload: true);
        Assert.Equal(second, timestamps[2]);
        Assert.Empty(changes);
        Assert.Same(changed, cache[0]);
        Assert.True(cache.IsLoaded);
        Assert.All(commands, query => Assert.True(query.WasDisposed));
        if (asynchronous)
        {
            await cache.InvalidateAndLoadAsync(null!);
        }
        else
        {
            cache.InvalidateAndLoad(null!);
        }
        Assert.Null(timestamps[3]);
        Assert.Empty(cache);

        async Task Load(bool reload = false)
        {
            if (asynchronous)
            {
                if (reload)
                {
                    await cache.ReloadAsync(null!);
                }
                else
                {
                    await cache.LoadAsync(null!);
                }
            }
            else if (reload)
            {
                cache.Reload(null!);
            }
            else
            {
                cache.Load(null!);
            }
        }
    }

    [Fact]
    public async Task A_failed_apply_preserves_the_checkpoint_and_retries_idempotently()
    {
        List<DateTime?> timestamps = [];
        using var cache = CachedList.New<Item, int>((_, since) =>
        {
            timestamps.Add(since);
            return new ReaderCommand(_ => Task.FromResult(Rows(since is null ? [new(1, "A")] : [new(1, "B"), new(2, "C")])));
        }, item => item.Id);
        await cache.LoadAsync(null!);
        DateTime? original = ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc;
        bool fail = true;
        cache.ListChanged += (_, _) =>
        {
            if (fail)
            {
                fail = false;
                throw new IOException();
            }
        };
        await Assert.ThrowsAsync<IOException>(() => cache.ReloadAsync(null!));
        Assert.Equal(original, ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc);
        Assert.True(cache.IsLoaded);
        await cache.ReloadAsync(null!);
        Assert.Equal(original, timestamps[1]);
        Assert.Equal(original, timestamps[2]);
        Assert.Equal([new(1, "B"), new Item(2, "C")], cache);
        Assert.True(cache.IsLoaded);
    }

    [Fact]
    public async Task Cancellation_keeps_the_old_checkpoint_and_contents()
    {
        bool cancelRead = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = CachedList.New<Item, int>((_, _) => new ReaderCommand(async token =>
        {
            if (cancelRead)
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Rows([new(1, "A")]);
        }), item => item.Id);
        await cache.LoadAsync(null!);
        DateTime? original = ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc;
        Item row = cache[0];
        cancelRead = true;
        using var cancel = new CancellationTokenSource();
        Task loading = cache.ReloadAsync(null!, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.Equal(original, ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc);
        Assert.Same(row, cache[0]);
        Assert.False(cache.IsLoading);
        Assert.True(cache.IsLoaded);
        cancelRead = false;
        await cache.ReloadAsync(null!);
        Assert.True(cache.IsLoaded);
    }

    [Fact]
    public async Task Invalidation_during_a_delta_forces_a_follow_up_full_load()
    {
        List<DateTime?> timestamps = [];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = CachedList.New<Item, int>((_, since) =>
        {
            timestamps.Add(since);
            int read = timestamps.Count;
            return new ReaderCommand(async token =>
            {
                if (read == 2)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
                return Rows(read switch { 1 => [new(1, "A")], 2 => [new(2, "B")], _ => [new(3, "C")] });
            });
        }, item => item.Id);
        await cache.LoadAsync(null!);
        DateTime? original = ((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc;
        Task loading = cache.ReloadAsync(null!);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        IReloadableCache<DbConnection> reloadable = cache;
        Task full = reloadable.InvalidateAndLoadAsync(null!);
        Assert.Null(((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc);
        release.SetResult();
        await Task.WhenAll(loading, full).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([null, original, null], timestamps);
        Assert.Equal([new Item(3, "C")], cache);
        Assert.NotNull(((CommandCacheLoader<Item>)cache.Loader!).LastLoadedUtc);
    }

    [Fact]
    public async Task Concurrent_loads_join_and_invalidation_requests_one_more_pass()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var cache = CachedList.New<Item, int>((_, _) => new ReaderCommand(async token =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            return Rows([new(reads, "A")]);
        }), item => item.Id);
        Task first = cache.LoadAsync(null!);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task[] joined = Enumerable.Range(0, 20).Select(_ => cache.LoadAsync(null!)).ToArray();
        cache.Invalidate();
        release.SetResult();
        await Task.WhenAll(joined.Append(first)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, reads);
        Assert.Equal([2], cache.Select(item => item.Id));
    }

    private static DbDataReader Rows(Item[] items)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(Item.Id), typeof(int));
        table.Columns.Add(nameof(Item.Name), typeof(string));
        foreach (var item in items)
        {
            table.Rows.Add(item.Id, item.Name);
        }
        return table.CreateDataReader();
    }

    private class ReaderConnection : DbConnection
    {
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private class ReaderCommand(Func<CancellationToken, Task<DbDataReader>> read) : DbCommand
    {
        public bool WasDisposed { get; private set; }
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = new ReaderConnection();
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        public override void Cancel() { }
        public override void Prepare() { }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => read(CancellationToken.None).GetAwaiter().GetResult();
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) => read(cancellationToken);
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
