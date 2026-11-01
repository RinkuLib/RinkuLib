using Rinku.Tracking;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public sealed class TrackingListGreenfieldTests
{
    [Fact]
    public void Plain_rows_use_list_owned_added_provenance()
    {
        var list = new TrackingList<int>([1, 2])
        {
            3
        };

        Assert.Equal(1, list.AddedCount);
        Assert.Equal(new[] { 3 }, list.Added.ToArray());
        Assert.True(list.IsAddedAt(2));
    }

    [Fact]
    public void List_reports_item_changes_and_does_not_classify_added_items_as_updates()
    {
        var existing = new ChangeRow();
        var list = new TrackingList<ChangeRow>([existing]);

        Assert.False(list.HasChanges);
        Assert.Empty(list.GetChanged());

        existing.Changed = true;

        Assert.True(list.HasChanges);
        Assert.Equal([(existing, false)], list.GetChanged().ToArray());

        var added = new ChangeRow { Changed = true };
        list.Add(added);

        Assert.True(list.HasChanges);
        Assert.True(list.IsAddedAt(1));
        Assert.Equal([(existing, false), (added, true)], [.. list.GetChanged()]);
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
        var list = new TrackingList<NewStateRow>
        {
            item
        };

        Assert.True(list.IsAddedAt(0));
        Assert.True(list.ConfirmAddedAt(0));
        Assert.False(item.IsNew);
        Assert.False(list.IsAddedAt(0));
        Assert.Equal(1, item.InsertConfirmations);
    }

    [Fact]
    public void Runtime_new_state_is_observed_behind_narrow_static_interface()
    {
        INarrowRow capable = NewStateRow.New(1);
        INarrowRow plain = new PlainRow(2);
        var list = new TrackingList<INarrowRow>
        {
            capable,
            plain
        };

        Assert.True(list.IsAddedAt(0));
        Assert.True(list.IsAddedAt(1));

        ((NewStateRow)capable).ConfirmNew();

        Assert.False(list.IsAddedAt(0));
        Assert.True(list.IsAddedAt(1));
    }

    [Fact]
    public void Runtime_confirmation_capability_is_observed_behind_narrow_static_interface()
    {
        INarrowRow item = NewStateRow.New(1);
        var list = new TrackingList<INarrowRow>
        {
            item
        };

        Assert.True(list.ConfirmAddedAt(0));

        Assert.False(list.IsAddedAt(0));
        Assert.Equal(1, ((NewStateRow)item).InsertConfirmations);
    }

    [Fact]
    public void Removing_an_added_row_does_not_create_removed_state()
    {
        var list = new TrackingList<int>
        {
            4
        };

        list.RemoveAt(0);

        Assert.Empty(list);
        Assert.Empty(list.Removed);
        Assert.False(list.HasChanges);
    }

    [Fact]
    public void Removing_baseline_row_tracks_removed_state()
    {
        var list = new TrackingList<int>([1, 2]);

        list.Remove(2);

        Assert.Equal(new[] { 2 }, list.Removed.ToArray());
    }

    [Fact]
    public void Readding_a_plain_removed_row_restores_it()
    {
        var list = new TrackingList<int>([1]);
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
        var list = new TrackingList<NewStateRow>([old], comparer: NewStateRowIdComparer.Instance);
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
        var list = new TrackingList<NewStateRow>([old], comparer: NewStateRowIdComparer.Instance);
        list.Remove(old);

        list.Add(replacement);

        Assert.Equal(new[] { old }, [.. list.Removed]);
        Assert.Equal(new[] { replacement }, [.. list.Added]);
    }

    [Fact]
    public void Insert_capability_failure_keeps_list_owned_added_state()
    {
        var item = new OperationRow(9) { InsertResult = false };
        var list = new TrackingList<OperationRow>
        {
            item
        };

        Assert.False(list.ConfirmAddedAt(0));
        Assert.True(list.IsAddedAt(0));
        Assert.Equal(1, item.InsertCalls);
    }

    [Fact]
    public void Insert_capability_success_clears_list_owned_added_state()
    {
        var item = new OperationRow(9);
        var list = new TrackingList<OperationRow>
        {
            item
        };

        Assert.True(list.ConfirmAddedAt(0));
        Assert.False(list.IsAddedAt(0));
        Assert.Equal(1, item.InsertCalls);
    }

    [Fact]
    public void Confirm_edit_does_not_accept_list_owned_addition()
    {
        var item = new OperationRow(9);
        var list = new TrackingList<OperationRow>
        {
            item
        };

        Assert.True(list.ConfirmEditAt(0));
        Assert.True(list.IsAddedAt(0));
        Assert.Equal(1, item.UpdateCalls);
        Assert.Equal(0, item.InsertCalls);
    }

    [Fact]
    public void Failed_delete_keeps_removed_observation()
    {
        var item = new OperationRow(1) { DeleteResult = false };
        var list = new TrackingList<OperationRow>([item]);
        list.RemoveAt(0);

        Assert.False(list.ConfirmDeleteAt(0));
        Assert.Equal(new[] { item }, [.. list.Removed]);
        Assert.Equal(1, item.DeleteCalls);
    }

    [Fact]
    public void Successful_delete_forgets_removed_observation()
    {
        var item = new OperationRow(1);
        var list = new TrackingList<OperationRow>([item]);
        list.RemoveAt(0);

        Assert.True(list.ConfirmDeleteAt(0));
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Move_carries_list_owned_provenance()
    {
        var list = new TrackingList<int>([1, 2]);
        list.Insert(1, 3);

        list.Move(1, 2);

        Assert.Equal(new[] { 1, 2, 3 }, list.ToArray());
        Assert.True(list.IsAddedAt(2));
        Assert.False(list.IsAddedAt(1));
    }

    [Fact]
    public void Clear_tracks_only_baseline_rows_as_removed()
    {
        var list = new TrackingList<int>([1, 2])
        {
            3
        };

        list.Clear();

        Assert.Equal(new[] { 1, 2 }, list.Removed.ToArray());
        Assert.Empty(list.Added);
    }

    [Fact]
    public void Default_factory_can_create_a_public_parameterless_reference_type()
    {
        var list = new TrackingList<Constructible>();

        Constructible created = list.AddNew();

        Assert.Same(created, list[0]);
        Assert.True(list.IsAddedAt(0));
    }

    [Fact]
    public void Custom_factory_owns_new_item_creation()
    {
        var list = new TrackingList<int>(newItemFactory: static () => 42);

        Assert.Equal(42, list.AddNew());
        Assert.Equal(42, list[0]);
    }

    [Fact]
    public void Confirm_changes_is_per_item_and_keeps_only_failures()
    {
        var one = new SelectiveRow(1);
        var two = new SelectiveRow(2);
        var three = new SelectiveRow(3);
        var four = new SelectiveRow(4);
        var list = new TrackingList<SelectiveRow>([one, two]);
        list.Remove(one);
        list.Add(three);
        list.Add(four);

        Assert.False(list.ConfirmChanges());

        Assert.Equal(new[] { one }, [.. list.Removed]);
        Assert.Equal(new[] { four }, [.. list.Added]);
    }

    [Fact]
    public void Confirmation_callbacks_are_operation_specific_and_pass_active_indexes()
    {
        var list = new TrackingList<int>([10]);
        var observed = new List<string>();
        using IDisposable subscription = list.SubscribeConfirmations((index, item) => observed.Add($"add:{index}:{item}"), (index, item) => observed.Add($"update:{index}:{item}"), item => observed.Add($"delete:{item}"));
        list.Add(20);
        list.Remove(10);

        Assert.True(list.ConfirmAddedAt(0));
        Assert.True(list.ConfirmEditAt(0));
        Assert.True(list.ConfirmDeleteAt(0));

        Assert.Equal(new[] { "add:0:20", "update:0:20", "delete:10" }, observed);
    }

    [Fact]
    public void Disposed_confirmation_subscription_is_not_called()
    {
        var list = new TrackingList<int>();
        int calls = 0;
        IDisposable first = list.SubscribeConfirmations(onAdded: (_, _) => calls++);
        using IDisposable second = list.SubscribeConfirmations(onAdded: (_, _) => calls += 10);
        first.Dispose();
        list.Add(1);

        Assert.True(list.ConfirmAddedAt(0));

        Assert.Equal(10, calls);
    }

    [Fact]
    public void Callback_failure_happens_after_tracking_confirmation()
    {
        var list = new TrackingList<int>
        {
            1
        };
        using IDisposable subscription = list.SubscribeConfirmations(onAdded: static (_, _) => throw new ConfirmedCallbackException());

        Assert.Throws<ConfirmedCallbackException>(() => list.ConfirmAddedAt(0));

        Assert.False(list.IsAddedAt(0));
    }

    [Fact]
    public void Value_type_capability_is_invoked_by_reference()
    {
        var list = new TrackingList<OperationStruct>([new OperationStruct()]);

        Assert.True(list.ConfirmEditAt(0));

        Assert.Equal(1, list[0].UpdateCalls);
    }

    [Fact]
    public void Editable_is_an_insert_and_update_capability_without_an_adapter()
    {
        var item = new EditableRow();
        var existing = new TrackingList<EditableRow>([item]);
        var added = new TrackingList<EditableRow>
        {
            item
        };

        Assert.True(existing.ConfirmEditAt(0));
        Assert.True(added.ConfirmAddedAt(0));

        Assert.Equal(2, item.ConfirmCalls);
    }

    private interface INarrowRow { int Id { get; } }
    private sealed class PlainRow(int id) : INarrowRow
    {
        public int Id { get; } = id;
    }

    private sealed class NewStateRow : INarrowRow, ITrackingListNewState, ICanInsert
    {
        private NewStateRow(int id, bool isNew) { Id = id; IsNew = isNew; }
        public int Id { get; }
        public bool IsNew { get; private set; }
        public int InsertConfirmations { get; private set; }
        public static NewStateRow Existing(int id) => new(id, false);
        public static NewStateRow New(int id) => new(id, true);
        public void ConfirmNew() => IsNew = false;
        public bool ConfirmInsert() { InsertConfirmations++; ConfirmNew(); return true; }
    }

    private sealed class NewStateRowIdComparer : IEqualityComparer<NewStateRow>
    {
        public static NewStateRowIdComparer Instance { get; } = new();
        public bool Equals(NewStateRow? x, NewStateRow? y) => x?.Id == y?.Id;
        public int GetHashCode(NewStateRow obj) => obj.Id;
    }

    private sealed class OperationRow(int id) : ICanInsert, ICanUpdate, ICanDelete
    {
        public int Id { get; } = id;
        public bool InsertResult { get; set; } = true;
        public bool UpdateResult { get; set; } = true;
        public bool DeleteResult { get; set; } = true;
        public int InsertCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool ConfirmInsert() { InsertCalls++; return InsertResult; }
        public bool ConfirmUpdate() { UpdateCalls++; return UpdateResult; }
        public bool ConfirmDelete() { DeleteCalls++; return DeleteResult; }
    }

    private sealed class SelectiveRow(int id) : ICanInsert, ICanUpdate, ICanDelete
    {
        public int Id { get; } = id;
        public bool ConfirmInsert() => Id != 4;
        public bool ConfirmUpdate() => true;
        public bool ConfirmDelete() => Id != 1;
    }

    private sealed class Constructible { }
    private sealed class ConfirmedCallbackException : Exception { }

    private sealed class ChangeRow : ITrackingChanges
    {
        public bool Changed { get; set; }
        public int TrackedMemberCount => 1;
        public bool TryGetChange(int memberIndex, out TrackingChange change)
        {
            if (memberIndex == 0 && Changed)
            {
                change = new(nameof(Changed), false, true);
                return true;
            }

            change = default;
            return false;
        }
    }

    private struct OperationStruct : ICanUpdate
    {
        public int UpdateCalls { get; private set; }
        public bool ConfirmUpdate() { UpdateCalls++; return true; }
    }

    private sealed class EditableRow : IEditable
    {
        public int ConfirmCalls { get; private set; }
        public bool IsEditing => true;
        public bool EnsureEditing() => true;
        public bool ConfirmEdit() { ConfirmCalls++; return true; }
        public bool CancelEdit() => true;
    }
}
