using System.ComponentModel;
using Rinku.Caching;

namespace Rinku.More.Tests;

public class CacheProjectionTests
{
    [Fact]
    public async Task Projection_loads_its_source_with_the_same_context_and_follows_changes()
    {
        var loader = new ContextLoader();
        using var source = CachedList.New(loader);
        using var projection = CachedList.Project(source, value => value * 2, EqualityComparer<int>.Default);
        using var nested = CachedList.Project(projection, value => value + 1, EqualityComparer<int>.Default);
        Assert.False(source.IsLoaded);
        Assert.False(nested.IsLoaded);
        Assert.Empty(nested);

        await nested.LoadAsync(3);
        Assert.True(nested.IsLoaded);
        Assert.Equal([3], loader.Contexts);
        Assert.Equal([7], nested);
        source.Add(4);
        Assert.Equal([7, 9], nested);
        source.Add(3);
        Assert.Equal([7, 9], nested);
        source[0] = 5;
        Assert.Equal([11, 9, 7], nested);
        source.RemoveAt(2);
        Assert.Equal([11, 9], nested);

        source.Invalidate();
        Assert.False(nested.IsLoaded);
        nested.Load(6);
        Assert.Equal([3, 6], loader.Contexts);
        Assert.Equal([13], nested);
        await nested.InvalidateAndLoadAsync(8);
        Assert.Equal([3, 6, 8], loader.Contexts);
        Assert.Equal([17], nested);
        nested.Dispose();
        source.Add(9);
        Assert.Equal([17], nested);
        Assert.Equal(0, loader.DisposeCalls);
        Assert.Equal([16, 18], projection);
    }

    [Fact]
    public async Task A_cache_can_supply_the_loader_contract_even_when_it_is_already_loaded()
    {
        using var source = CachedList.New(new ContextLoader());
        source.Load(7);
        using var target = CachedList.New(source);
        Assert.False(target.IsLoaded);
        await target.LoadAsync(8);
        Assert.Equal([7], target);
        await target.InvalidateAndLoadAsync(9);
        Assert.Equal([9], source);
        Assert.Equal([9], target);
    }

    [Fact]
    public async Task Projection_loads_join_the_source_load_and_joined_cancellation_does_not_cancel_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var source = CachedList.New(new TestCacheLoader<int>(async token =>
        {
            Interlocked.Increment(ref reads);
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return [3];
        }));
        using var projection = CachedList.Project(source, value => value * 2, EqualityComparer<int>.Default);
        Task loading = source.LoadAsync(0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancel = new CancellationTokenSource();
        Task canceled = projection.LoadAsync(0, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Task retry = projection.LoadAsync(0);
        release.SetResult();
        await Task.WhenAll(loading, retry).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, reads);
        Assert.Equal([6], projection);
        Assert.True(projection.IsLoaded);
    }

    [Fact]
    public void Dictionary_projects_an_ordinary_binding_list_and_has_no_loading_capability()
    {
        var source = new BindingList<(string Key, int Value)> { ("A", 1), ("B", 2), ("a", 3) };
        using var dictionary = new ProjectedBindingDictionary<(string Key, int Value), string, int>(source, row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase);
        Assert.False((object)dictionary is ILazyLoaded);
        IBindingList binding = dictionary;
        Assert.Equal([nameof(KeyValuePair<string, int>.Key), nameof(KeyValuePair<string, int>.Value)], ((ITypedList)dictionary).GetItemProperties(null).Cast<PropertyDescriptor>().Select(property => property.Name));
        Assert.Equal(2, dictionary.Count);
        Assert.Equal(3, dictionary["A"]);
        Assert.Equal(new KeyValuePair<string, int>("A", 3), binding[0]);
        int notifications = 0;
        dictionary.ListChanged += (_, _) => notifications++;
        source[2] = ("B", 4);
        Assert.Equal(1, dictionary["A"]);
        Assert.Equal(4, dictionary["B"]);
        source.RemoveAt(0);
        Assert.False(dictionary.ContainsKey("A"));
        var copy = new KeyValuePair<string, int>[dictionary.Count];
        binding.CopyTo(copy, 0);
        Assert.Equal(dictionary, copy);
        Assert.Equal(2, notifications);
        dictionary.Dispose();
        source.Clear();
        Assert.Equal(2, notifications);
        Assert.Single(dictionary);
    }

    [Fact]
    public void Dictionary_reads_and_typed_enumeration_allocate_nothing()
    {
        var source = new BindingList<int> { 1, 2, 3 };
        using var dictionary = new ProjectedBindingDictionary<int, int, int>(source, value => value, value => value * 2);
        for (int i = 0; i < 100; i++)
        {
            Read();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sum += Read();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(20_000, sum);

        int Read()
        {
            int result = dictionary.Count + dictionary[2];
            result += dictionary.ContainsKey(1) ? 1 : 0;
            result += dictionary.TryGetValue(3, out int value) ? value : 0;
            foreach (var item in dictionary)
            {
                result += item.Key;
            }
            return result;
        }
    }

    private sealed class ContextLoader : ICacheLoader<int, int>
    {
        public List<int> Contexts { get; } = [];
        public bool IsLoaded { get; private set; }
        public int DisposeCalls { get; private set; }
        public void Invalidate() => IsLoaded = false;
        public void Load(int context, ICacheCollection<int> target)
        {
            Contexts.Add(context);
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
