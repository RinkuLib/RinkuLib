using System.ComponentModel;
using System.Reflection;
using Rinku.Tracking.Runtime;
using Xunit;

namespace Rinku.Tracking.Tests;

public sealed class RuntimeTrackingGreenfieldTests
{
    [Fact]
    public void Default_generated_item_reads_without_starting_edit()
    {
        var original = new Employee { Id = 7, Name = "A" };
        IRuntimeTrackingItem<Employee> edit = RuntimeTracking.Default<Employee>().Create(original);

        Assert.Equal("A", edit.Get<string>(nameof(Employee.Name)));
        Assert.False(edit.IsEditing);
        Assert.False(edit.IsNew);
        Assert.True(edit.TryGetOriginal(out Employee? returned));
        Assert.NotNull(returned);
        Assert.Same(original, returned);
    }

    [Fact]
    public void First_set_allocates_the_separate_snapshot()
    {
        var original = new Employee { Id = 7, Name = "A" };
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().GetRegistration<IEmployeeEdit>();
        IEmployeeEdit edit = registration.Create(original);
        FieldInfo editField = RequireField(registration.GeneratedType, "_edit");

        Assert.Null(editField.GetValue(edit));

        edit.Name = "B";

        Assert.NotNull(editField.GetValue(edit));
        Assert.True(((IEditable)edit).IsEditing);
        Assert.Equal("A", original.Name);
        Assert.Equal("B", edit.Name);
    }

    [Fact]
    public void New_generated_item_has_original_and_no_snapshot_until_edited()
    {
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().GetRegistration<IEmployeeEdit>();
        ITrackingListContext<IEmployeeEdit> context = registration.CreateContext();

        Assert.True(context.CanCreateNew);
        IEmployeeEdit edit = context.CreateNew();
        FieldInfo editField = RequireField(registration.GeneratedType, "_edit");

        Assert.True(((ITrackingListNewState)edit).IsNew);
        Assert.False(((IEditable)edit).IsEditing);
        Assert.Null(editField.GetValue(edit));
        Assert.True(((IOriginal<Employee>)edit).TryGetOriginal(out Employee? original));
        Assert.NotNull(original);
        Assert.Equal(0, original.Id);
    }

    [Fact]
    public void Generated_context_confirms_edit_and_new_state_together_for_added_item()
    {
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().GetRegistration<IEmployeeEdit>();
        ITrackingListContext<IEmployeeEdit> context = registration.CreateContext();
        IEmployeeEdit edit = context.CreateNew();
        edit.Name = "B";

        Assert.True(context.ConfirmAdded(edit));

        Assert.False(((ITrackingListNewState)edit).IsNew);
        Assert.False(((IEditable)edit).IsEditing);
        Assert.True(((IOriginal<Employee>)edit).TryGetOriginal(out Employee? original));
        Assert.NotNull(original);
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Ensure_editing_is_success_and_is_idempotent()
    {
        IRuntimeTrackingItem<Employee> edit = RuntimeTracking.Default<Employee>().Create(new Employee { Name = "A" });

        Assert.True(edit.EnsureEditing());
        Assert.True(edit.IsEditing);
        Assert.True(edit.EnsureEditing());
        Assert.True(edit.IsEditing);
    }

    [Fact]
    public void Cancel_discards_snapshot_without_touching_original()
    {
        var original = new Employee { Name = "A" };
        IEmployeeEdit edit = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().GetRegistration<IEmployeeEdit>().Create(original);
        edit.Name = "B";

        Assert.True(((IEditable)edit).CancelEdit());

        Assert.Equal("A", edit.Name);
        Assert.Equal("A", original.Name);
        Assert.False(((IEditable)edit).IsEditing);
    }

    [Fact]
    public void Confirm_applies_only_actual_changes()
    {
        var original = new CountingEmployee { Name = "A" };
        original.ResetWrites();
        ICountingEmployeeEdit edit = RuntimeTracking.CreateOptions<CountingEmployee, ICountingEmployeeEdit>().GetRegistration<ICountingEmployeeEdit>().Create(original);
        edit.Name = "B";
        edit.Name = "A";

        Assert.True(((IEditable)edit).ConfirmEdit());

        Assert.Equal(0, original.NameWrites);
        Assert.Equal("A", original.Name);
    }

    [Fact]
    public void Change_enumeration_uses_effective_comparison_and_preserves_changed_to_null()
    {
        var original = new Employee { Name = "A" };
        IEmployeeEdit edit = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().GetRegistration<IEmployeeEdit>().Create(original);
        var changes = (ITrackingChanges)edit;

        edit.Name = "B";
        edit.Name = "A";
        Assert.False(changes.HasChanges());

        edit.Name = null;
        TrackingChange change = Assert.Single(changes.GetChanges());
        Assert.Equal(nameof(Employee.Name), change.Name);
        Assert.Equal("A", change.OriginalValue);
        Assert.Null(change.Value);
    }

    [Fact]
    public void Readonly_original_member_is_exposed_but_not_in_snapshot()
    {
        var original = new ReadOnlyIdEmployee(8) { Name = "A" };
        RuntimeTrackingRegistration<ReadOnlyIdEmployee, IReadOnlyIdEmployeeEdit> registration = RuntimeTracking.CreateOptions<ReadOnlyIdEmployee, IReadOnlyIdEmployeeEdit>().GetRegistration<IReadOnlyIdEmployeeEdit>();
        IReadOnlyIdEmployeeEdit edit = registration.Create(original);
        Type snapshotType = RequireField(registration.GeneratedType, "_edit").FieldType;

        Assert.Equal(8, edit.Id);
        Assert.Single(snapshotType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void Getter_only_interface_does_not_remove_original_editability()
    {
        var original = new Employee { Name = "A" };
        RuntimeTrackingRegistration<Employee, IEmployeeView> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeView>().GetRegistration<IEmployeeView>();
        IEmployeeView view = registration.Create(original);
        PropertyInfo generatedName = RequireProperty(registration.GeneratedType, nameof(Employee.Name));

        Assert.NotNull(generatedName.SetMethod);
        Assert.True(((IRuntimeMemberAccess)view).Set(nameof(Employee.Name), "B"));
        Assert.True(((IEditable)view).IsEditing);
        Assert.Equal("B", view.Name);
        Assert.Equal("A", original.Name);
    }

    [Fact]
    public void Multiple_interfaces_are_applied_to_the_same_generated_type()
    {
        RuntimeTrackingOptions<Employee> options = RuntimeTracking.CreateOptions<Employee>();
        options.Apply<IEmployeeName>();
        options.Apply<IEmployeeId>();
        IEmployeeName edit = options.GetRegistration<IEmployeeName>().Create(new Employee { Id = 3, Name = "A" });

        Assert.IsAssignableFrom<IEmployeeId>(edit);
        Assert.Equal(3, ((IEmployeeId)edit).Id);
    }

    [Fact]
    public void Impossible_interface_fails_generation_instead_of_inventing_state()
    {
        RuntimeTrackingOptions<Employee> options = RuntimeTracking.CreateOptions<Employee>();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => options.GetRegistration<IMissingMember>());

        Assert.Contains(nameof(IMissingMember.Foo), error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_is_direct_tedit_state_and_survives_edit_lifecycle()
    {
        RuntimeTrackingOptions<Employee> options = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>();
        options.Metadata<Employee, string>();
        IEmployeeEdit edit = options.GetRegistration<IEmployeeEdit>().Create(new Employee { Name = "A" });
        var writer = (IMetadataWriter<string>)edit;
        var reader = (IMetadataReader<string>)edit;
        writer.SetMetadata("meta");

        edit.Name = "B";
        ((IEditable)edit).CancelEdit();
        Assert.Equal("meta", reader.Metadata);

        edit.Name = "C";
        ((IEditable)edit).ConfirmEdit();
        Assert.Equal("meta", reader.Metadata);
    }

    [Fact]
    public void Normal_attributes_are_copied_to_generated_properties()
    {
        RuntimeTrackingRegistration<AttributedEmployee, IAttributedEmployeeEdit> registration = RuntimeTracking.CreateOptions<AttributedEmployee, IAttributedEmployeeEdit>().GetRegistration<IAttributedEmployeeEdit>();
        PropertyDescriptor property = TypeDescriptor.GetProperties(registration.GeneratedType)[nameof(AttributedEmployee.Name)]
            ?? throw new InvalidOperationException("Generated Name descriptor is missing.");

        Assert.Equal("Employee name", property.DisplayName);
        Assert.Equal("Identity", property.Category);
    }

    [Fact]
    public void Nested_read_does_not_start_edit_and_ensure_edit_detaches_it()
    {
        var artist = new Artist { Name = "A" };
        var album = new Album { Artist = artist };
        RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
        options.Member<Artist>(nameof(Album.Artist)).NestedEdit();
        IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(album);

        Assert.Same(artist, edit.Artist);
        Assert.False(((IEditable)edit).IsEditing);

        Assert.True(((IEditable)edit).EnsureEditing());
        Assert.NotSame(artist, edit.Artist);
        edit.Artist.Name = "B";
        Assert.Equal("A", artist.Name);

        ((IEditable)edit).ConfirmEdit();
        Assert.Same(artist, album.Artist);
        Assert.Equal("B", artist.Name);
    }

    [Fact]
    public void Nested_runtime_path_write_ensures_parent_edit_before_mutation()
    {
        var artist = new Artist { Name = "A" };
        var album = new Album { Artist = artist };
        RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
        options.Member<Artist>(nameof(Album.Artist)).NestedEdit();
        IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(album);
        var runtime = (IRuntimeMemberAccess)edit;

        Assert.True(runtime.Set($"{nameof(Album.Artist)}.{nameof(Artist.Name)}", "B"));
        Assert.True(((IEditable)edit).IsEditing);
        Assert.Equal("A", artist.Name);
        Assert.Equal("B", runtime.Get<string>($"{nameof(Album.Artist)}.{nameof(Artist.Name)}"));
    }


    [Fact]
    public void List_source_context_confirms_add_edit_and_delete_back_to_original_list()
    {
        var original = new Employee { Id = 1, Name = "A" };
        var source = new List<Employee> { original };
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>();

        list[0].Name = "B";
        Assert.True(list.ConfirmEditAt(0));
        Assert.Same(original, source[0]);
        Assert.Equal("B", source[0].Name);

        IEmployeeEdit added = list.AddNew();
        added.Name = "C";
        Assert.True(list.ConfirmAddedAt(1));
        Assert.Equal(2, source.Count);
        Assert.Equal("C", source[1].Name);
        Assert.False(((ITrackingListNewState)added).IsNew);

        list.RemoveAt(0);
        Assert.True(list.ConfirmDeleteAt(0));
        Assert.Single(source);
        Assert.Equal("C", source[0].Name);
    }

    [Fact]
    public void Array_source_context_can_confirm_edits_but_cannot_confirm_structural_changes()
    {
        var original = new Employee { Id = 1, Name = "A" };
        Employee[] source = [original];
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>();

        list[0].Name = "B";
        Assert.True(list.ConfirmEditAt(0));
        Assert.Same(original, source[0]);
        Assert.Equal("B", source[0].Name);

        IEmployeeEdit added = list.AddNew();
        added.Name = "C";
        Assert.False(list.ConfirmAddedAt(1));
        Assert.True(list.IsAddedAt(1));

        list.RemoveAt(0);
        Assert.False(list.ConfirmDeleteAt(0));
        Assert.Single(list.Removed);
        Assert.Same(original, source[0]);
    }

    [Fact]
    public void Source_slot_mapping_does_not_use_original_equality()
    {
        var first = new EqualEmployee(7) { Name = "First" };
        var second = new EqualEmployee(7) { Name = "Second" };
        var source = new List<EqualEmployee> { first, second };
        TrackingList<IEqualEmployeeEdit> list = source.ToTrackingList<EqualEmployee, IEqualEmployeeEdit>();

        list[1].Name = "Edited second";
        Assert.True(list.ConfirmEditAt(1));

        Assert.Equal("First", first.Name);
        Assert.Equal("Edited second", second.Name);
        Assert.Same(first, source[0]);
        Assert.Same(second, source[1]);
    }

    [Fact]
    public void New_item_can_confirm_edit_before_it_has_a_source_slot()
    {
        var source = new List<Employee>();
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>();
        IEmployeeEdit added = list.AddNew();
        added.Name = "A";

        Assert.True(list.ConfirmEditAt(0));
        Assert.True(((ITrackingListNewState)added).IsNew);
        Assert.False(((IEditable)added).IsEditing);
        Assert.Empty(source);

        Assert.True(list.ConfirmAddedAt(0));
        Assert.Single(source);
        Assert.Equal("A", source[0].Name);
    }

    [Fact]
    public void Generated_snapshot_is_a_separate_hidden_container_with_only_edit_members()
    {
        RuntimeTrackingRegistration<ReadOnlyIdEmployee, IReadOnlyIdEmployeeEdit> registration = RuntimeTracking.CreateOptions<ReadOnlyIdEmployee, IReadOnlyIdEmployeeEdit>().GetRegistration<IReadOnlyIdEmployeeEdit>();
        FieldInfo editField = RequireField(registration.GeneratedType, "_edit");

        Assert.NotEqual(registration.GeneratedType, editField.FieldType);
        Assert.False(editField.FieldType.IsPublic);
        Assert.DoesNotContain(registration.GeneratedType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            static field => field.Name.Contains("Name", StringComparison.OrdinalIgnoreCase) && field.Name != "_edit");
    }

    private static FieldInfo RequireField(Type type, string name)
        => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);

    private static PropertyInfo RequireProperty(Type type, string name)
        => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, name);

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

    public interface IEmployeeView
    {
        string? Name { get; }
    }

    public interface IEmployeeName { string? Name { get; } }
    public interface IEmployeeId { int Id { get; } }
    public interface IMissingMember { string Foo { get; set; } }

    public sealed class ReadOnlyIdEmployee(int id)
    {
        public int Id { get; } = id;
        public string? Name { get; set; }
    }

    public interface IReadOnlyIdEmployeeEdit
    {
        int Id { get; }
        string? Name { get; set; }
    }

    public sealed class CountingEmployee
    {
        private string? _name;
        public int NameWrites { get; private set; }
        public string? Name { get => _name; set { _name = value; NameWrites++; } }
        public void ResetWrites() => NameWrites = 0;
    }

    public interface ICountingEmployeeEdit { string? Name { get; set; } }

    public sealed class AttributedEmployee
    {
        [DisplayName("Employee name")]
        [Category("Identity")]
        public string? Name { get; set; }
    }

    public interface IAttributedEmployeeEdit { string? Name { get; set; } }


    public sealed class EqualEmployee(int id)
    {
        public int Id { get; } = id;
        public string? Name { get; set; }

        public override bool Equals(object? obj) => obj is EqualEmployee other && other.Id == Id;
        public override int GetHashCode() => Id;
    }

    public interface IEqualEmployeeEdit
    {
        int Id { get; }
        string? Name { get; set; }
    }

    public sealed class Album
    {
        public Artist Artist { get; set; } = new();
    }

    public sealed class Artist
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public interface IAlbumEdit
    {
        Artist Artist { get; }
    }
}
