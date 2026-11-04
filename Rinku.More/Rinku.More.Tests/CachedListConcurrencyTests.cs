using System.Collections;
using System.ComponentModel;
using Rinku.Caching;

namespace Rinku.More.Tests;

public class CachedListConcurrencyTests
{
    [Fact]
    public void Reading_does_not_allocate_or_copy_the_list()
    {
        using var items = CachedList.New<int>();
        items.SetItems(Enumerable.Range(1, 64));
        IReadOnlyList<int> indexed = items;
        int[] destination = new int[items.Count];
        for (int step = 0; step < 100; step++)
        {
            Read(items, indexed, destination);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int step = 0; step < 1000; step++)
        {
            sum += Read(items, indexed, destination);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(4_257_000, sum);
    }

    [Fact]
    public async Task Concurrent_edits_deliver_ordered_binding_events()
    {
        using var items = CachedList.New<int>();
        List<int> mirror = [];
        int notifications = 0;
        items.ListChanged += (_, change) =>
        {
            switch (change.ListChangedType)
            {
                case ListChangedType.ItemAdded:
                    mirror.Insert(change.NewIndex, items[change.NewIndex]);
                    break;
                case ListChangedType.ItemChanged:
                    mirror[change.NewIndex] = items[change.NewIndex];
                    break;
                case ListChangedType.ItemDeleted:
                    mirror.RemoveAt(change.NewIndex);
                    break;
                case ListChangedType.Reset:
                    mirror.Clear();
                    mirror.AddRange(items);
                    break;
            }
            notifications++;
            Assert.Equal(items, mirror);
        };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int step = 0; step < 500; step++)
            {
                int value = worker * 500 + step;
                items.Add(value);
                lock (((ICollection)items).SyncRoot)
                {
                    int index = items.IndexOf(value);
                    items[index] = value + 2000;
                    items.RemoveAt(index);
                }
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Empty(items);
        Assert.Empty(mirror);
        Assert.Equal(6000, notifications);
    }

    [Fact]
    public async Task Locked_enumeration_and_copy_remain_consistent_during_replacement()
    {
        using var items = CachedList.New<int>();
        items.SetItems(Enumerable.Repeat(1, 32));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int step = 0; step < 1000; step++)
            {
                if (worker == 0)
                {
                    items.SetItems(Enumerable.Repeat(step + 1, 32));
                }
                else
                {
                    int[] copy = new int[32];
                    lock (((ICollection)items).SyncRoot)
                    {
                        int expected = items[0];
                        foreach (int value in items)
                        {
                            Assert.Equal(expected, value);
                        }
                        items.CopyTo(copy, 0);
                    }
                    Assert.All(copy, value => Assert.Equal(copy[0], value));
                }
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task External_lock_keeps_traversal_consistent_while_the_count_changes()
    {
        using var items = CachedList.New<int>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int step = 0; step < 1000; step++)
            {
                if (worker == 0)
                {
                    items.SetItems(Enumerable.Range(1, step % 64));
                }
                else
                {
                    lock (((ICollection)items).SyncRoot)
                    {
                        int expected = 1;
                        foreach (int value in items)
                        {
                            Assert.Equal(expected++, value);
                        }
                        Assert.Equal(items.Count, expected - 1);
                    }
                }
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task External_lock_protects_compound_list_operations()
    {
        using var items = CachedList.New<int>();
        ICollection collection = items;
        Assert.True(collection.IsSynchronized);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int step = 0; step < 500; step++)
            {
                lock (collection.SyncRoot)
                {
                    int next = items.Count == 0 ? 0 : items[items.Count - 1] + 1;
                    items.Add(next);
                }
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(Enumerable.Range(0, 2000), items);
    }

    [Fact]
    public async Task Projections_follow_concurrent_source_replacements()
    {
        using var source = CachedList.New<int>();
        using var list = CachedList.Project<int, int, object?>(source, item => item % 4, EqualityComparer<int>.Default);
        using var dictionary = new ProjectedBindingDictionary<int, int, int>(source, item => item % 4, item => item);
        list.ListChanged += (_, _) => Assert.Equal(source.Select(item => item % 4).Distinct(), list);
        dictionary.ListChanged += (_, _) =>
        {
            var expected = source.GroupBy(item => item % 4).ToDictionary(group => group.Key, group => group.Last());
            Assert.Equal(expected.OrderBy(item => item.Key), dictionary.OrderBy(item => item.Key));
            foreach (var entry in dictionary)
            {
                Assert.Equal(entry.Value, dictionary[entry.Key]);
            }
        };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int step = 0; step < 300; step++)
            {
                source.SetItems(Enumerable.Range(worker * 1000 + step, 8));
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(source.Select(item => item % 4).Distinct(), list);
    }

    [Fact]
    public async Task Dictionary_replacements_publish_both_views_together()
    {
        using var source = CachedList.New<(string Key, int Value)>();
        using var items = new ProjectedBindingDictionary<(string Key, int Value), string, int>(source, row => row.Key, row => row.Value);
        source.SetItems([("A", 1), ("B", 1)]);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int step = 0; step < 500; step++)
            {
                if (worker == 0)
                {
                    source.SetItems([("A", step), ("B", step)]);
                }
                else
                {
                    lock (((ICollection)items).SyncRoot)
                    {
                        Assert.Equal(items["A"], items["B"]);
                        foreach (var row in (IEnumerable<KeyValuePair<string, int>>)items)
                        {
                            Assert.Equal(row.Value, items[row.Key]);
                        }
                    }
                }
            }
        }))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(499, items["A"]);
        Assert.Throws<NotSupportedException>(() => ((IList)items).Add(new KeyValuePair<string, int>("C", 3)));
        Assert.Throws<NotSupportedException>(() => ((IList)items).Clear());
    }

    [Fact]
    public async Task An_independent_loader_coalesces_loads_and_reloads_after_invalidation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var items = CachedList.New(new TestCacheLoader<int>(async token =>
        {
            int read = Interlocked.Increment(ref reads);
            if (read == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            return [read];
        }));
        Task owner = items.LoadAsync(0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task[] joined = Enumerable.Range(0, 20).Select(_ => items.LoadAsync(0)).ToArray();
        items.Add(99);
        items.Invalidate();
        release.SetResult();
        await Task.WhenAll(joined.Append(owner)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(2, reads);
        Assert.Equal([2], items);
        Assert.True(items.IsLoaded);
        items.Invalidate();
        items.Load(0);
        Assert.Equal(3, reads);
        Assert.Equal([3], items);
    }

    [Fact]
    public async Task Synchronous_and_asynchronous_reloads_share_one_active_loader()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        int active = 0;
        using var items = CachedList.New(new TestCacheLoader<int>(async token =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            int read = Interlocked.Increment(ref reads);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            Interlocked.Decrement(ref active);
            return [read];
        }));
        Task first = items.ReloadAsync(0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task joined = items.ReloadAsync(0);
        Task synchronous = Task.Factory.StartNew(() => items.Reload(0), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        release.SetResult();
        await Task.WhenAll(first, joined, synchronous).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.InRange(reads, 1, 2);
        items.Reload(0);
        await items.ReloadAsync(0);
        Assert.Equal([reads], items);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task A_canceled_reload_does_not_enter_the_loader()
    {
        int reads = 0;
        using var items = CachedList.New(new TestCacheLoader<int>(_ => Task.FromResult(new[] { ++reads })));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => items.ReloadAsync(0, cancellation.Token));
        Assert.Equal(0, reads);
        Assert.False(items.IsLoading);
        await items.LoadAsync(0);
        Assert.Equal(1, reads);
        Assert.True(items.IsLoaded);
    }

    [Fact]
    public async Task Canceling_a_joined_load_does_not_cancel_the_independent_loader()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var items = CachedList.New(new TestCacheLoader<int>(async token =>
        {
            await release.Task.WaitAsync(token);
            return [1];
        }));
        Task owner = items.LoadAsync(0);
        using var cancel = new CancellationTokenSource();
        Task joined = items.LoadAsync(0, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joined);
        release.SetResult();
        await owner.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([1], items);
        Assert.True(items.IsLoaded);
    }

    private static int Read(CachedList<int, object?> items, IReadOnlyList<int> indexed, int[] destination)
    {
        lock (((ICollection)items).SyncRoot)
        {
            int sum = items.Count + items.IndexOf(32);
            sum += items.Contains(32) ? 1 : 0;
            sum += items.IsLoaded ? 1 : 0;
            sum += items.IsLoading ? 1 : 0;
            items.CopyTo(destination, 0);
            foreach (int value in items)
            {
                sum += value;
            }
            for (int index = 0; index < indexed.Count; index++)
            {
                sum += indexed[index];
            }
            return sum;
        }
    }
}
