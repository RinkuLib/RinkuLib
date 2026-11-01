using System.ComponentModel;
using System.Reflection;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;
using Rinku.Querying.Parameters;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public sealed class RuntimeTrackingGreenfieldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_partial_interface_preserves_undeclared_members_and_runtime_command_mapping(bool binding)
    {
        var original = new Employee { Id = 7, Name = "Original" };
        Employee[] source = [original];
        TrackingList<IEmployeeId> rows = binding ? source.ToBindingList<Employee, IEmployeeId>() : source.ToTrackingList<Employee, IEmployeeId>();
        IEmployeeId edit = rows[0];
        var members = Assert.IsAssignableFrom<IRuntimeMemberAccess>(edit);
        Assert.NotNull(edit.GetType().GetProperty(nameof(Employee.Name)));
        Assert.Equal(7, edit.Id);
        Assert.True(members.Set(nameof(Employee.Name), "Changed"));
        var readName = MethodCaller.Create<Func<IRuntimeMemberAccess, string?>>(ReadEmployeeName);
        Assert.Equal("Changed", readName(members));
        Assert.Equal("Original", original.Name);
        Assert.True(rows.ConfirmEditAt(0));
        Assert.Equal("Changed", original.Name);

        var readId = MethodCaller.Create<Func<IRuntimeMemberAccess, int>>(ReadEmployeeId);
        int total = 0;
        for (int i = 0; i < 10000; i++)
        {
            total += readId(members);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            total += readId(members);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(140000, total);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Runtime_command_mapping_honors_names_aliases_exclusions_and_runtime_only_values()
    {
        var options = RuntimeTracking.CreateOptions<ParameterEmployee>();
        options.Member<string>("Extra").SnapshotValue().Parameters();
        var edit = options.GetRegistration<ITrackingItem<ParameterEmployee>>().Create(new() { Id = 7 });
        edit.Set("Extra", "Runtime");
        var read = MethodCaller.Create<Func<IRuntimeMemberAccess, (int, string?)>>(ReadEmployeeParameters);
        Assert.Equal((7, (string?)null), read(edit));
        var extra = MethodCaller.Create<Func<IRuntimeMemberAccess, string>>(ReadExtra);
        Assert.Equal("Runtime", extra(edit));
        var ignored = MethodCaller.Create<Func<IRuntimeMemberAccess, string>>(ReadHidden);
        Assert.Throws<InvalidOperationException>(() => ignored(edit));

        var excludedOptions = RuntimeTracking.CreateOptions<Employee>();
        excludedOptions.Member<string>(nameof(Employee.Name)).Parameters(false);
        var excluded = excludedOptions.GetRegistration<ITrackingItem<Employee>>().Create(new() { Name = "Visible" });
        Assert.Equal("Visible", excluded.Get<string>(nameof(Employee.Name)));
        var name = MethodCaller.Create<Func<IRuntimeMemberAccess, string?>>(ReadEmployeeName);
        Assert.Throws<InvalidOperationException>(() => name(excluded));
    }

    private static int ReadEmployeeId(int Id) => Id;
    private static string? ReadEmployeeName(string? Name) => Name;
    private static (int, string?) ReadEmployeeParameters(int Key, string? Text) => (Key, Text);
    private static string ReadExtra(string Extra) => Extra;
    private static string ReadHidden(string Hidden) => Hidden;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Materialization_supports_default_plain_and_tracking_interfaces(bool binding)
    {
        Employee[] originals = [new() { Name = "Default" }, new() { Name = "Plain" }, new() { Name = "Tracking" }];
        Employee[] defaultSource = [originals[0]];
        Employee[] plainSource = [originals[1]];
        Employee[] trackingSource = [originals[2]];
        TrackingList<ITrackingItem<Employee>> defaults = binding ? defaultSource.ToBindingList() : defaultSource.ToTrackingList();
        TrackingList<IEmployeeEdit> plain = binding ? plainSource.ToBindingList<Employee, IEmployeeEdit>() : plainSource.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());
        TrackingList<ITrackedEmployeeEdit> tracking = binding ? trackingSource.ToBindingList<Employee, ITrackedEmployeeEdit>() : trackingSource.ToTrackingList<Employee, ITrackedEmployeeEdit>();

        Assert.False(typeof(ITrackingItem<Employee>).IsAssignableFrom(typeof(IEmployeeEdit)));
        Assert.True(plain[0] is IRuntimeMemberAccess);
        ITrackingItem<Employee>[] edits = [defaults[0], Assert.IsAssignableFrom<ITrackingItem<Employee>>(plain[0]), tracking[0]];
        for (int i = 0; i < edits.Length; i++)
        {
            ITrackingItem<Employee> edit = edits[i];
            Assert.True(edit.TryGetOriginal(out Employee? original));
            Assert.Same(originals[i], original);
            Assert.Equal(originals[i].Name, edit.Get<string>(nameof(Employee.Name)));
            Assert.True(edit.Set(nameof(Employee.Name), "Changed"));
            Assert.True(edit.IsEditing);
            Assert.NotEqual("Changed", originals[i].Name);
            Assert.True(edit.ConfirmEdit());
            Assert.Equal("Changed", originals[i].Name);
            Assert.False(edit.IsEditing);
        }
        Assert.Equal("Changed", plain[0].Name);
        Assert.Equal("Changed", tracking[0].Name);
    }

    [Fact]
    public void Default_generated_item_reads_without_starting_edit()
    {
        var original = new Employee { Id = 7, Name = "A" };
        ITrackingItem<Employee> edit = RuntimeTracking.Default<Employee>().Create(original);

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
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert().GetRegistration<IEmployeeEdit>();
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
    public void New_generated_item_has_no_original_and_stores_its_values_in_the_edit_buffer()
    {
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert().GetRegistration<IEmployeeEdit>();

        IEmployeeEdit edit = registration.CreateNew();
        FieldInfo editField = RequireField(registration.GeneratedType, "_edit");

        Assert.True(((ITrackingListNewState)edit).IsNew);
        Assert.True(((IEditable)edit).IsEditing);
        Assert.NotNull(editField.GetValue(edit));
        Assert.False(((IOriginal<Employee>)edit).TryGetOriginal(out Employee? original));
        Assert.Null(original);
    }

    [Fact]
    public void Generated_insert_capability_confirms_edit_and_new_state_together()
    {
        RuntimeTrackingRegistration<Employee, IEmployeeEdit> registration = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert().GetRegistration<IEmployeeEdit>();
        var list = new TrackingList<IEmployeeEdit>(newItemFactory: registration.CreateNew);
        IEmployeeEdit edit = list.AddNew();
        edit.Name = "B";

        Assert.True(list.ConfirmAddedAt(0));

        Assert.False(((ITrackingListNewState)edit).IsNew);
        Assert.False(((IEditable)edit).IsEditing);
        Assert.True(((IOriginal<Employee>)edit).TryGetOriginal(out Employee? original));
        Assert.NotNull(original);
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Ensure_editing_is_success_and_is_idempotent()
    {
        ITrackingItem<Employee> edit = RuntimeTracking.Default<Employee>().Create(new Employee { Name = "A" });

        Assert.True(edit.EnsureEditing());
        Assert.True(edit.IsEditing);
        Assert.True(edit.EnsureEditing());
        Assert.True(edit.IsEditing);
    }

    [Fact]
    public void Cancel_discards_snapshot_without_touching_original()
    {
        var original = new Employee { Name = "A" };
        IEmployeeEdit edit = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert().GetRegistration<IEmployeeEdit>().Create(original);
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
        IEmployeeEdit edit = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert().GetRegistration<IEmployeeEdit>().Create(original);
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

        Assert.IsType<IEmployeeId>(edit, exactMatch: false);
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
    public void Additional_direct_member_survives_edit_lifecycle()
    {
        RuntimeTrackingOptions<Employee> options = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert();
        options.Member<string>("Notes", member => member.Direct().Parameters(false));
        IEmployeeEdit edit = options.GetRegistration<IEmployeeEdit>().Create(new Employee { Name = "A" });
        var reader = Assert.IsAssignableFrom<IRuntimeMemberAccess>(edit);
        reader.Set("Notes", "meta");

        edit.Name = "B";
        ((IEditable)edit).CancelEdit();
        Assert.Equal("meta", reader.Get<string>("Notes"));

        edit.Name = "C";
        ((IEditable)edit).ConfirmEdit();
        Assert.Equal("meta", reader.Get<string>("Notes"));
    }

    [Fact]
    public void Result_validation_is_distinct_from_additional_member_storage()
    {
        int calls = 0;
        RuntimeTrackingOptions<Employee> options = RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert();
        options.Member<string>("Notes", member => member.Direct().Parameters(false));
        options.ValidateResult<Employee, IEmployeeEdit, string>((edit, result) => { calls++; return edit.Name == result; });
        IEmployeeEdit edit = options.GetRegistration<IEmployeeEdit>().Create(new Employee { Name = "A" });

        var validation = Assert.IsType<IResultValidatable<string>>(edit, exactMatch: false);
        Assert.IsAssignableFrom<IRuntimeMemberAccess>(edit);
        Assert.True(validation.ValidateResult("A"));
        Assert.False(validation.ValidateResult("B"));
        Assert.Equal(2, calls);
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
    public void List_bindback_reflects_confirmed_add_edit_and_delete()
    {
        var original = new Employee { Id = 1, Name = "A" };
        var source = new List<Employee> { original };
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());

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
    public void Readonly_bindback_failure_occurs_after_tracking_confirmation()
    {
        var original = new Employee { Id = 1, Name = "A" };
        Employee[] source = [original];
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());

        list[0].Name = "B";
        Assert.True(list.ConfirmEditAt(0));
        Assert.Same(original, source[0]);
        Assert.Equal("B", source[0].Name);

        IEmployeeEdit added = list.AddNew();
        added.Name = "C";
        Assert.Throws<NotSupportedException>(() => list.ConfirmAddedAt(1));
        Assert.False(list.IsAddedAt(1));
        Assert.False(((ITrackingListNewState)added).IsNew);

        list.RemoveAt(0);
        Assert.Throws<NotSupportedException>(() => list.ConfirmDeleteAt(0));
        Assert.Empty(list.Removed);
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
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());
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
    public void Bindback_uses_matching_update_index_without_calling_indexof()
    {
        var original = new Employee { Name = "A" };
        var source = new CountingList<Employee>(original);
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());
        source.ResetIndexOfCalls();
        list[0].Name = "B";

        Assert.True(list.ConfirmEditAt(0));

        Assert.Equal(0, source.IndexOfCalls);
        Assert.Equal("B", original.Name);
    }

    [Fact]
    public void Bindback_falls_back_to_indexof_when_update_index_no_longer_matches()
    {
        var first = new Employee { Name = "A" };
        var second = new Employee { Name = "B" };
        var source = new CountingList<Employee>(first, second);
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());
        source.ResetIndexOfCalls();
        list.Move(1, 0);
        list[0].Name = "edited";

        Assert.True(list.ConfirmEditAt(0));

        Assert.Equal(1, source.IndexOfCalls);
        Assert.Equal("A", first.Name);
        Assert.Equal("edited", second.Name);
    }

    [Fact]
    public void Delete_bindback_locates_the_original_without_an_index_hint()
    {
        var original = new Employee { Name = "A" };
        var source = new CountingList<Employee>(original);
        TrackingList<IEmployeeEdit> list = source.ToTrackingList<Employee, IEmployeeEdit>(RuntimeTracking.CreateOptions<Employee, IEmployeeEdit>().ConstructOnInsert());
        source.ResetIndexOfCalls();
        list.RemoveAt(0);

        Assert.True(list.ConfirmDeleteAt(0));

        Assert.Equal(1, source.IndexOfCalls);
        Assert.Empty(source);
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

    public sealed class ParameterEmployee
    {
        [ParameterAlias("Key")]
        public int Id { get; set; }
        [ParameterName("Text")]
        public string? Name { get; set; }
        [ParameterIgnore]
        public string Hidden { get; set; } = "Hidden";
    }

    public interface IEmployeeEdit
    {
        int Id { get; }
        string? Name { get; set; }
    }

    public interface ITrackedEmployeeEdit : IEmployeeEdit, ITrackingItem<Employee>
    {
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
        Artist 
            Artist { get; }
    }

    private sealed class CountingList<T>(params T[] items) : IList<T>
    {
        private readonly List<T> _items = [.. items];
        public int IndexOfCalls { get; private set; }
        public T this[int index] { get => _items[index]; set => _items[index] = value; }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(T item) => _items.Add(item);
        public void Clear() => _items.Clear();
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(T item) { IndexOfCalls++; return _items.IndexOf(item); }
        public void Insert(int index, T item) => _items.Insert(index, item);
        public bool Remove(T item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void ResetIndexOfCalls() => IndexOfCalls = 0;
    }
}
