using System.Collections;
using System.Collections.ObjectModel;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="EnumerableCountProvider"/> answers the count of a non-generic
/// <see cref="IEnumerable"/> without enumerating, when the concrete type exposes one.
/// </summary>
public class EnumerableCountTests {
    [Fact]
    public void Array_reports_its_length() {
        IEnumerable source = new[] { 1, 2, 3 };
        Assert.True(source.TryGetNonEnumeratedCount(out var count));
        Assert.Equal(3, count);
    }

    [Fact]
    public void List_reports_its_count() {
        IEnumerable source = new List<string> { "a", "b" };
        Assert.True(source.TryGetNonEnumeratedCount(out var count));
        Assert.Equal(2, count);
    }

    [Fact]
    public void HashSet_reports_through_the_generic_collection_contract() {
        IEnumerable source = new HashSet<int> { 1, 2, 3, 4 };
        Assert.True(source.TryGetNonEnumeratedCount(out var count));
        Assert.Equal(4, count);
    }

    [Fact]
    public void ReadOnlyCollection_reports_its_count() {
        IEnumerable source = new ReadOnlyCollection<int>([1, 2]);
        Assert.True(source.TryGetNonEnumeratedCount(out var count));
        Assert.Equal(2, count);
    }

    [Fact]
    public void Lazy_iterator_cannot_report_without_enumerating() {
        IEnumerable source = Numbers();
        Assert.False(source.TryGetNonEnumeratedCount(out var count));
        Assert.Equal(0, count);

        static IEnumerable<int> Numbers() {
            yield return 1;
        }
    }

    [Fact]
    public void Repeated_lookups_hit_the_cached_delegate() {
        IEnumerable first = new List<double> { 1.0 };
        IEnumerable second = new List<double> { 1.0, 2.0, 3.0 };
        Assert.True(first.TryGetNonEnumeratedCount(out var firstCount));
        Assert.True(second.TryGetNonEnumeratedCount(out var secondCount));
        Assert.Equal(1, firstCount);
        Assert.Equal(3, secondCount);
    }

    [Fact]
    public void Custom_contract_can_teach_a_new_shape() {
        var contract = new GenericCountContract(typeof(ISizeCarrier<>), nameof(ISizeCarrier<>.Size));
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, int>>();
        Assert.True(contract.TryGetDelegate(typeof(ISizeCarrier<int>), cache, out var getter));
        Assert.NotNull(getter);
        Assert.Equal(9, getter(new SizedThing(9)));
        Assert.False(contract.TryGetDelegate(typeof(IEnumerable<int>), cache, out _));
    }
}

public interface ISizeCarrier<T> {
    int Size { get; }
}
public class SizedThing(int size) : ISizeCarrier<int> {
    public int Size { get; } = size;
}