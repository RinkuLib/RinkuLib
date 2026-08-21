using System.ComponentModel;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;
using Xunit;

namespace Rinku.Tracking.Tests;

public sealed class BindingTrackingGreenfieldTests
{
    [Fact]
    public void Typed_list_exposes_generated_runtime_properties()
    {
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Id = 1, Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>();
        PropertyDescriptorCollection properties = ((ITypedList)list).GetItemProperties(null);

        Assert.NotNull(properties[nameof(Employee.Id)]);
        Assert.NotNull(properties[nameof(Employee.Name)]);
        Assert.Equal(typeof(int), properties[nameof(Employee.Id)]?.PropertyType);
    }

    [Fact]
    public void Add_new_uses_context_creation_without_starting_edit()
    {
        BindingTrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToBindingList<Employee, IEmployeeEdit>();

        IEmployeeEdit edit = list.AddNew();

        Assert.True(((ITrackingListNewState)edit).IsNew);
        Assert.False(((IEditable)edit).IsEditing);
        Assert.True(list.IsAddedAt(0));
    }

    [Fact]
    public void Cancel_new_removes_pending_add_without_removed_observation()
    {
        BindingTrackingList<IEmployeeEdit> list = Array.Empty<Employee>().ToBindingList<Employee, IEmployeeEdit>();
        list.AddNew();

        ((ICancelAddNew)list).CancelNew(0);

        Assert.Empty(list);
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Begin_edit_forces_snapshot_but_end_edit_does_not_confirm_it()
    {
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>();
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
        BindingTrackingList<IEmployeeEdit> list = new[] { new Employee { Name = "A" } }.ToBindingList<Employee, IEmployeeEdit>();
        ListChangedEventArgs? observed = null;
        list.ListChanged += (_, args) => observed = args;

        list[0].Name = "B";

        Assert.NotNull(observed);
        Assert.Equal(ListChangedType.ItemChanged, observed?.ListChangedType);
        Assert.Equal(0, observed?.NewIndex);
        Assert.Equal(nameof(Employee.Name), observed?.PropertyDescriptor?.Name);
    }

    [Fact]
    public void Binding_list_context_confirms_added_item_back_to_original_source()
    {
        var source = new BindingList<Employee>();
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>();
        IEmployeeEdit edit = list.AddNew();
        edit.Name = "B";

        Assert.True(list.ConfirmAddedAt(0));

        Assert.Single(source);
        Assert.Equal("B", source[0].Name);
        Assert.False(((ITrackingListNewState)edit).IsNew);
        Assert.False(list.IsAddedAt(0));
    }

    [Fact]
    public void Binding_list_context_confirms_edit_back_to_original_source()
    {
        var original = new Employee { Name = "A" };
        var source = new BindingList<Employee> { original };
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>();
        list[0].Name = "B";

        Assert.True(list.ConfirmEditAt(0));

        Assert.Same(original, source[0]);
        Assert.Equal("B", source[0].Name);
    }

    [Fact]
    public void Binding_list_context_confirms_delete_back_to_original_source()
    {
        var original = new Employee { Name = "A" };
        var source = new BindingList<Employee> { original };
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>();
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
        BindingTrackingList<IEmployeeEdit> list = source.ToBindingList<Employee, IEmployeeEdit>();
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
