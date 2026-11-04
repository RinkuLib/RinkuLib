using System.Collections;
using System.ComponentModel;
using Rinku.Caching;

namespace Rinku.More.Tests;

/// <summary>Tests cached-list loading, mutation, and binding behavior.</summary>
public class CachedListTests
{
    /// <summary>Verifies mutable list operations raise binding notifications.</summary>
    [Fact]
    public void Mutable_list_operations_raise_binding_notifications()
    {
        var list = CachedList.New<int>();
        list.SetItems([]);
        List<ListChangedType> changes = [];
        list.ListChanged += (_, args) => changes.Add(args.ListChangedType);

        list.Add(1);
        list.AddRange([2, 3]);
        list.Insert(1, 4);
        list[0] = 5;
        Assert.True(list.Remove(2));
        list.RemoveAt(0);
        list.SetItems([8, 9]);
        list.Clear();

        Assert.Empty(list);
        Assert.Equal([ListChangedType.ItemAdded, ListChangedType.Reset, ListChangedType.ItemAdded, ListChangedType.ItemChanged, ListChangedType.ItemDeleted, ListChangedType.ItemDeleted, ListChangedType.Reset, ListChangedType.Reset], changes);
    }

    /// <summary>Verifies generic and binding interfaces expose the same contents.</summary>
    [Fact]
    public void Generic_and_binding_interfaces_share_the_same_contents()
    {
        var list = CachedList.New<string>();
        list.SetItems([]);
        IList nonGeneric = list;
        IList<string> generic = list;
        IReadOnlyList<string> readOnly = list;
        IBindingList binding = list;

        nonGeneric.Add("A");
        generic.Add("B");

        Assert.Equal(2, readOnly.Count);
        Assert.Equal("A", binding[0]);
        Assert.IsType<ICancelAddNew>(list, exactMatch: false);
        Assert.IsType<IRaiseItemChangedEvents>(list, exactMatch: false);
    }

    /// <summary>Verifies add-new is unsupported by default.</summary>
    [Fact]
    public void AddNew_is_unsupported_by_default()
    {
        var list = CachedList.New<BindableItem>();
        IBindingList binding = list;

        Assert.False(binding.AllowNew);
        Assert.Throws<NotSupportedException>(() => binding.AddNew());
    }

    /// <summary>Verifies item property changes are unsupported by default.</summary>
    [Fact]
    public void Item_property_changes_are_unsupported_by_default()
    {
        var item = new BindableItem();
        var list = CachedList.New<BindableItem>();
        ListChangedEventArgs? observed = null;
        list.Add(item);
        list.ListChanged += (_, args) => observed = args;

        item.Name = "changed";

        Assert.Null(observed);
        Assert.False(((IRaiseItemChangedEvents)list).RaisesItemChangedEvents);
    }

    [Fact]
    public void Editing_the_list_invalidates_its_enumerator()
    {
        var list = CachedList.New<int>();
        list.SetItems([1, 2]);
        var enumerator = list.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        list.Add(3);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    private sealed class BindableItem : INotifyPropertyChanged
    {
        private string? _name;
        public string? Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new(nameof(Name)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

}
