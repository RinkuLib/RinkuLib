using Rinku.Tracking;
using Xunit;

namespace Rinku.Tracking.Tests;

public sealed class TrackingListGreenfieldTests
{
    [Fact]
    public void Plain_rows_use_list_owned_added_provenance()
    {
        var list = new TrackingList<int>(new[] { 1, 2 });

        list.Add(3);

        Assert.Equal(1, list.AddedCount);
        Assert.Equal(new[] { 3 }, list.Added.ToArray());
        Assert.True(list.IsAddedAt(2));
    }

    [Fact]
    public void Added_count_is_derived_not_stored()
    {
        Assert.DoesNotContain(typeof(TrackingList<int>).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            static field => field.Name.Contains("addedCount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Item_owned_new_state_is_observed_without_list_writes()
    {
        var item = NewStateRow.New(7);
        var context = new NewStateContext();
        var list = new TrackingList<NewStateRow>(context: context);
        list.Add(item);

        Assert.True(list.IsAddedAt(0));
        Assert.True(list.ConfirmAddedAt(0));
        Assert.False(item.IsNew);
        Assert.False(list.IsAddedAt(0));
        Assert.Equal(1, context.AddedConfirmations);
    }

    [Fact]
    public void Runtime_new_state_is_observed_behind_narrow_static_interface()
    {
        INarrowRow capable = NewStateRow.New(1);
        INarrowRow plain = new PlainRow(2);
        var list = new TrackingList<INarrowRow>();

        list.Add(capable);
        list.Add(plain);

        Assert.True(list.IsAddedAt(0));
        Assert.True(list.IsAddedAt(1));

        ((NewStateRow)capable).ConfirmNew();

        Assert.False(list.IsAddedAt(0));
        Assert.True(list.IsAddedAt(1));
    }

    [Fact]
    public void Removing_an_added_row_does_not_create_removed_state()
    {
        var list = new TrackingList<int>();
        list.Add(4);

        list.RemoveAt(0);

        Assert.Empty(list);
        Assert.Empty(list.Removed);
        Assert.False(list.HasChanges);
    }

    [Fact]
    public void Removing_baseline_row_tracks_removed_state()
    {
        var list = new TrackingList<int>(new[] { 1, 2 });

        list.Remove(2);

        Assert.Equal(new[] { 2 }, list.Removed.ToArray());
    }

    [Fact]
    public void Readding_a_plain_removed_row_restores_it()
    {
        var list = new TrackingList<int>(new[] { 1 });
        list.Remove(1);

        list.Add(1);

        Assert.Empty(list.Removed);
        Assert.Empty(list.Added);
        Assert.False(list.HasChanges);
    }

    [Fact]
    public void Accepted_item_owned_row_can_restore_an_equal_removed_row()
    {
        var old = NewStateRow.Existing(12);
        var replacement = NewStateRow.Existing(12);
        var list = new TrackingList<NewStateRow>(new[] { old }, comparer: NewStateRowIdComparer.Instance);
        list.Remove(old);

        list.Add(replacement);

        Assert.Empty(list.Removed);
        Assert.Empty(list.Added);
        Assert.Same(replacement, list[0]);
    }

    [Fact]
    public void New_item_owned_row_does_not_restore_an_equal_removed_row()
    {
        var old = NewStateRow.Existing(12);
        var replacement = NewStateRow.New(12);
        var list = new TrackingList<NewStateRow>(new[] { old }, comparer: NewStateRowIdComparer.Instance);
        list.Remove(old);

        list.Add(replacement);

        Assert.Equal(new[] { old }, list.Removed.ToArray());
        Assert.Equal(new[] { replacement }, list.Added.ToArray());
    }

    [Fact]
    public void Context_failure_keeps_list_owned_added_state()
    {
        var context = new RecordingContext<int> { AddedResult = false };
        var list = new TrackingList<int>(context: context);
        list.Add(9);

        Assert.False(list.ConfirmAddedAt(0));
        Assert.True(list.IsAddedAt(0));
    }

    [Fact]
    public void Context_success_clears_only_list_owned_added_state()
    {
        var context = new RecordingContext<int>();
        var list = new TrackingList<int>(context: context);
        list.Add(9);

        Assert.True(list.ConfirmAddedAt(0));
        Assert.False(list.IsAddedAt(0));
        Assert.Equal(1, context.AddedCalls);
    }

    [Fact]
    public void Confirm_edit_does_not_accept_list_owned_addition()
    {
        var context = new RecordingContext<int>();
        var list = new TrackingList<int>(context: context);
        list.Add(9);

        Assert.True(list.ConfirmEditAt(0));
        Assert.True(list.IsAddedAt(0));
        Assert.Equal(1, context.EditCalls);
        Assert.Equal(0, context.AddedCalls);
    }

    [Fact]
    public void Failed_delete_keeps_removed_observation()
    {
        var context = new RecordingContext<int> { DeleteResult = false };
        var list = new TrackingList<int>(new[] { 1 }, context: context);
        list.RemoveAt(0);

        Assert.False(list.ConfirmDeleteAt(0));
        Assert.Equal(new[] { 1 }, list.Removed.ToArray());
    }

    [Fact]
    public void Successful_delete_forgets_removed_observation()
    {
        var context = new RecordingContext<int>();
        var list = new TrackingList<int>(new[] { 1 }, context: context);
        list.RemoveAt(0);

        Assert.True(list.ConfirmDeleteAt(0));
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Move_carries_list_owned_provenance()
    {
        var list = new TrackingList<int>(new[] { 1, 2 });
        list.Insert(1, 3);

        list.Move(1, 2);

        Assert.Equal(new[] { 1, 2, 3 }, list.ToArray());
        Assert.True(list.IsAddedAt(2));
        Assert.False(list.IsAddedAt(1));
    }

    [Fact]
    public void Clear_tracks_only_baseline_rows_as_removed()
    {
        var list = new TrackingList<int>(new[] { 1, 2 });
        list.Add(3);

        list.Clear();

        Assert.Equal(new[] { 1, 2 }, list.Removed.ToArray());
        Assert.Empty(list.Added);
    }

    [Fact]
    public void Default_context_can_create_a_public_parameterless_reference_type()
    {
        var list = new TrackingList<Constructible>();

        Constructible created = list.AddNew();

        Assert.Same(created, list[0]);
        Assert.True(list.IsAddedAt(0));
    }

    [Fact]
    public void Custom_context_owns_new_item_creation()
    {
        var context = new RecordingContext<int> { CanCreate = true, Created = 42 };
        var list = new TrackingList<int>(context: context);

        Assert.Equal(42, list.AddNew());
        Assert.Equal(42, list[0]);
    }

    [Fact]
    public void Confirm_changes_is_per_item_and_keeps_only_failures()
    {
        var context = new SelectiveContext();
        var list = new TrackingList<int>(new[] { 1, 2 }, context: context);
        list.Remove(1);
        list.Add(3);
        list.Add(4);

        Assert.False(list.ConfirmChanges());

        Assert.Equal(new[] { 1 }, list.Removed.ToArray());
        Assert.Equal(new[] { 4 }, list.Added.ToArray());
    }

    private interface INarrowRow { int Id { get; } }

    private sealed class PlainRow(int id) : INarrowRow
    {
        public int Id { get; } = id;
    }

    private sealed class NewStateRow : INarrowRow, ITrackingListNewState
    {
        private NewStateRow(int id, bool isNew) { Id = id; IsNew = isNew; }
        public int Id { get; }
        public bool IsNew { get; private set; }
        public static NewStateRow Existing(int id) => new(id, false);
        public static NewStateRow New(int id) => new(id, true);
        public void ConfirmNew() => IsNew = false;
    }

    private sealed class NewStateContext : ITrackingListContext<NewStateRow>
    {
        public int AddedConfirmations { get; private set; }
        public bool CanCreateNew => true;
        public NewStateRow CreateNew() => NewStateRow.New(0);
        public bool ConfirmAdded(NewStateRow item) { AddedConfirmations++; item.ConfirmNew(); return true; }
        public bool ConfirmEdit(NewStateRow item) => true;
        public bool ConfirmDelete(NewStateRow item) => true;
    }

    private sealed class NewStateRowIdComparer : IEqualityComparer<NewStateRow>
    {
        public static NewStateRowIdComparer Instance { get; } = new();
        public bool Equals(NewStateRow? x, NewStateRow? y) => x?.Id == y?.Id;
        public int GetHashCode(NewStateRow obj) => obj.Id;
    }

    private sealed class RecordingContext<T> : ITrackingListContext<T>
    {
        public bool AddedResult { get; set; } = true;
        public bool EditResult { get; set; } = true;
        public bool DeleteResult { get; set; } = true;
        public bool CanCreate { get; set; }
        public T? Created { get; set; }
        public int AddedCalls { get; private set; }
        public int EditCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool CanCreateNew => CanCreate;
        public T CreateNew() => CanCreate && Created is T created ? created : throw new NotSupportedException();
        public bool ConfirmAdded(T item) { AddedCalls++; return AddedResult; }
        public bool ConfirmEdit(T item) { EditCalls++; return EditResult; }
        public bool ConfirmDelete(T item) { DeleteCalls++; return DeleteResult; }
    }

    private sealed class SelectiveContext : ITrackingListContext<int>
    {
        public bool CanCreateNew => true;
        public int CreateNew() => 0;
        public bool ConfirmAdded(int item) => item != 4;
        public bool ConfirmEdit(int item) => true;
        public bool ConfirmDelete(int item) => item != 1;
    }

    private sealed class Constructible { }
}
