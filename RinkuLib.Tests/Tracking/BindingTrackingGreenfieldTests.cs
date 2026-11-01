using System.ComponentModel;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public sealed class BindingTrackingGreenfieldTests
{
    [Fact]
    public void Insert_confirmation_notifies_the_new_state_once_and_reuses_property_arguments()
    {
        var list = Array.Empty<Employee>().ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        var first = list.AddNew();
        var second = list.AddNew();
        PropertyChangedEventArgs? firstName = null;
        PropertyChangedEventArgs? secondName = null;
        int newStateChanges = 0;
        ((INotifyPropertyChanged)first).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Employee.Name))
            {
                firstName = args;
            }
            if (args.PropertyName == nameof(ITrackingListNewState.IsNew))
            {
                Assert.False(((ITrackingListNewState)first).IsNew);
                newStateChanges++;
            }
        };
        ((INotifyPropertyChanged)second).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Employee.Name))
            {
                secondName = args;
            }
        };
        first.Name = "First";
        second.Name = "Second";
        Assert.NotNull(firstName);
        Assert.Same(firstName, secondName);
        Assert.True(((ICanInsert)first).ConfirmInsert());
        Assert.True(((ICanInsert)first).ConfirmInsert());
        Assert.Equal(1, newStateChanges);
    }

    [Fact]
    public void Typed_list_exposes_generated_runtime_properties()
    {
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Id = 1, Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        PropertyDescriptorCollection properties = ((ITypedList)list).GetItemProperties(null);

        Assert.NotNull(properties[nameof(Employee.Id)]);
        Assert.NotNull(properties[nameof(Employee.Name)]);
        Assert.Equal(typeof(int), properties[nameof(Employee.Id)]?.PropertyType);
    }

    [Fact]
    public void Add_new_starts_with_an_edit_buffer_and_no_original()
    {
        BindingTrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());

        IEmployeeEdit edit = list.AddNew();

        Assert.True(((ITrackingListNewState)edit).IsNew);
        Assert.True(((IEditable)edit).IsEditing);
        Assert.True(list.IsAddedAt(0));
    }

    [Fact]
    public void Cancel_new_removes_pending_add_without_removed_observation()
    {
        BindingTrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        list.AddNew();

        ((ICancelAddNew)list).CancelNew(0);

        Assert.Empty(list);
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Begin_edit_forces_snapshot_but_end_edit_does_not_confirm_it()
    {
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        IEmployeeEdit edit = list[0];
        var binding = (IEditableObject)edit;

        binding.BeginEdit();
        Assert.True(((IEditable)edit).IsEditing);
        edit.Name = "B";

        binding.EndEdit();

        Assert.True(((IEditable)edit).IsEditing);
        Assert.Equal("B", edit.Name);
    }

    [Fact]
    public void Binding_notifications_are_forwarded_as_item_changed()
    {
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        ListChangedEventArgs? observed = null;
        list.ListChanged += (_, args) => observed = args;

        list[0].Name = "B";

        Assert.NotNull(observed);
        Assert.Equal(ListChangedType.ItemChanged, observed?.ListChangedType);
        Assert.Equal(0, observed?.NewIndex);
        Assert.Equal(nameof(Employee.Name), observed?.PropertyDescriptor?.Name);
    }

    [Fact]
    public void Refreshing_baseline_moves_automatic_bindback_to_the_new_source()
    {
        var oldSource = new BindingList<Employee> { new() { Id = 1, Name = "Old" } };
        var list = oldSource.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        var refreshed = new BindingList<Employee> { new() { Id = 1, Name = "Reloaded" } };
        list.ResetBaseline(refreshed);
        list[0].Name = "Saved after refresh";
        Assert.True(list.ConfirmEditAt(0));
        Assert.Equal("Saved after refresh", refreshed[0].Name);
        Assert.Equal("Old", oldSource[0].Name);
        list.RemoveAt(0);
        Assert.True(list.ConfirmDeleteAt(0));
        Assert.Empty(refreshed);
        Assert.Single(oldSource);

        // A read-only replacement has no automatic bind-back target.
        list.ResetBaseline(new[] { new Employee { Id = 2, Name = "Array" } });
        list[0].Name = "Accepted";
        Assert.True(list.ConfirmEditAt(0));
        Assert.Empty(refreshed);
    }

    [Fact]
    public void Binding_list_bindback_confirms_added_item_back_to_original_list()
    {
        var source = new BindingList<Employee>();
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        IEmployeeEdit edit = list.AddNew();
        edit.Name = "B";

        Assert.True(list.ConfirmAddedAt(0));

        Assert.Single(source);
        Assert.Equal("B", source[0].Name);
        Assert.False(((ITrackingListNewState)edit).IsNew);
        Assert.False(list.IsAddedAt(0));
    }

    [Fact]
    public void Binding_list_bindback_confirms_edit_back_to_original_list()
    {
        var original = new Employee { Name = "A" };
        var source = new BindingList<Employee> { original };
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        list[0].Name = "B";

        Assert.True(list.ConfirmEditAt(0));

        Assert.Same(original, source[0]);
        Assert.Equal("B", source[0].Name);
    }

    [Fact]
    public void Binding_list_bindback_confirms_delete_back_to_original_list()
    {
        var original = new Employee { Name = "A" };
        var source = new BindingList<Employee> { original };
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        list.RemoveAt(0);

        Assert.True(list.ConfirmDeleteAt(0));

        Assert.Empty(source);
        Assert.Empty(list.Removed);
    }


    [Fact]
    public void Binding_source_without_item_notifications_gets_explicit_index_refresh_on_same_reference_confirm()
    {
        var original = new Employee { Name = "A" };
        var source = new BindingList<Employee> { original };
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().Binding().ConstructOnInsert());
        int itemChanged = 0;
        source.ListChanged += (_, args) =>
        {
            if (args.ListChangedType == ListChangedType.ItemChanged && args.NewIndex == 0) itemChanged++;
        };

        list[0].Name = "B";
        Assert.True(list.ConfirmEditAt(0));

        Assert.Equal("B", original.Name);
        Assert.Equal(1, itemChanged);
    }

    [Fact]
    public void Binding_source_with_inotify_does_not_get_duplicate_index_refresh()
    {
        var original = new NotifyEmployee { Name = "A" };
        var source = new BindingList<NotifyEmployee> { original };
        BindingTrackingList<INotifyEmployeeEdit> list = source.ToBindingList<NotifyEmployee, INotifyEmployeeEdit>();
        int itemChanged = 0;
        source.ListChanged += (_, args) =>
        {
            if (args.ListChangedType == ListChangedType.ItemChanged && args.NewIndex == 0) itemChanged++;
        };

        list[0].Name = "B";
        Assert.True(list.ConfirmEditAt(0));

        Assert.Equal("B", original.Name);
        Assert.Equal(1, itemChanged);
    }

    public sealed class Employee
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public interface IEmployeeEdit
    {
        int Id { get; }
        string? Name { get; set; }
    }

    public sealed class NotifyEmployee : INotifyPropertyChanged
    {
        private string? _name;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    public interface INotifyEmployeeEdit
    {
        string? Name { get; set; }
    }


}
