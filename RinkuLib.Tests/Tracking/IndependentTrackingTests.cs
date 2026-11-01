using Rinku.Tracking;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public class IndependentTrackingTests
{
    [Fact]
    public void An_item_can_be_created_edited_and_confirmed_without_any_list()
    {
        var item = RuntimeTracking.CreateOptions<Row, ITrackingItem<Row>>().ConstructOnInsert().GetRegistration<ITrackingItem<Row>>().CreateNew();
        var confirmations = new List<TrackingConfirmation>();
        item.Confirmed += (source, operation) =>
        {
            Assert.Same(item, source);
            Assert.False(source.IsNew);
            confirmations.Add(operation);
        };
        item.Set(nameof(Row.Name), "Created");
        Assert.True(item.IsNew);
        Assert.True(item.ConfirmInsert());
        Assert.False(item.IsNew);
        Assert.False(item.HasChanges());
        Assert.Equal(new[] { TrackingConfirmation.Insert }, confirmations);
        item.Set(nameof(Row.Name), "Updated");
        Assert.False(item.IsNew);
        Assert.True(item.ConfirmEdit());
        Assert.Equal(new[] { TrackingConfirmation.Insert, TrackingConfirmation.Update }, confirmations);
    }

    [Fact]
    public void Direct_item_confirmation_updates_containing_lists_and_the_original_collection_once()
    {
        var originals = new List<Row>();
        using var rows = originals.ToBindingList<Row, ITrackingItem<Row>>(RuntimeTracking.CreateOptions<Row, ITrackingItem<Row>>().Binding().ConstructOnInsert());
        var item = rows.AddNew();
        int inserted = 0;
        int updated = 0;
        using var subscription = rows.SubscribeConfirmations((_, _) => inserted++, (_, _) => updated++);
        item.Set(nameof(Row.Name), "Created");
        Assert.True(item.ConfirmInsert());
        Assert.Equal("Created", Assert.Single(originals).Name);
        Assert.Equal(1, inserted);
        Assert.Equal(0, updated);
        Assert.False(rows.HasChanges);
        item.Set(nameof(Row.Name), "Updated");
        Assert.True(item.ConfirmEdit());
        Assert.Equal("Updated", Assert.Single(originals).Name);
        Assert.Equal(1, updated);
        item.Set(nameof(Row.Name), "Through list");
        Assert.True(rows.ConfirmEditAt(0));
        Assert.Equal(2, updated);
    }

    [Fact]
    public void A_shared_item_notifies_each_list_and_reset_detaches_old_membership()
    {
        var item = RuntimeTracking.Default<Row>().Create(new Row { Name = "Original" });
        using var left = new TrackingList<ITrackingItem<Row>>([item]);
        using var right = new TrackingList<ITrackingItem<Row>>([item]);
        int leftUpdates = 0;
        int rightUpdates = 0;
        using var one = left.SubscribeConfirmations(onUpdated: (_, _) => leftUpdates++);
        using var two = right.SubscribeConfirmations(onUpdated: (_, _) => rightUpdates++);
        item.Set(nameof(Row.Name), "Changed");
        item.ConfirmEdit();
        Assert.Equal(1, leftUpdates);
        Assert.Equal(1, rightUpdates);
        left.ResetBaseline(Array.Empty<ITrackingItem<Row>>());
        item.Set(nameof(Row.Name), "Again");
        item.ConfirmEdit();
        Assert.Equal(1, leftUpdates);
        Assert.Equal(2, rightUpdates);
        right.Dispose();
        item.Set(nameof(Row.Name), "Still independent");
        item.ConfirmEdit();
        Assert.Equal(2, rightUpdates);
    }

    [Fact]
    public void Removing_an_unaccepted_item_releases_its_confirmation_subscription()
    {
        using var rows = Array.Empty<Row>().ToBindingList<Row, ITrackingItem<Row>>(RuntimeTracking.CreateOptions<Row, ITrackingItem<Row>>().Binding().ConstructOnInsert());
        var item = rows.AddNew();
        int inserted = 0;
        using var subscription = rows.SubscribeConfirmations((_, _) => inserted++);
        rows.Remove(item);
        item.Set(nameof(Row.Name), "Independent");
        Assert.True(item.ConfirmInsert());
        Assert.Equal(0, inserted);
        Assert.Empty(rows);
    }

    [Fact]
    public void Disposed_lists_reject_membership_changes_before_mutating_items()
    {
        var item = RuntimeTracking.CreateOptions<Row, ITrackingItem<Row>>().ConstructOnInsert().GetRegistration<ITrackingItem<Row>>().CreateNew();
        var rows = new TrackingList<ITrackingItem<Row>>();
        rows.Dispose();
        Assert.Throws<ObjectDisposedException>(() => rows.Add(item));
        Assert.Empty(rows);
        Assert.True(item.IsNew);
        Assert.Throws<ObjectDisposedException>(() => rows.ResetBaseline([item]));
        Assert.Empty(rows);
    }

    [Fact]
    public void Insert_construction_combines_output_parameters_with_edited_properties_and_fields() {
        var item = RuntimeTracking.CreateOptions<ConstructedRow, ITrackingItem<ConstructedRow>>().ConstructOnInsert().GetRegistration<ITrackingItem<ConstructedRow>>().CreateNew();
        item.PendingParameters = new TrackingParameters();
        item.PendingParameters.Set(nameof(ConstructedRow.Id), 42);
        item.Set(nameof(ConstructedRow.Name), "Edited");
        item.Set(nameof(ConstructedRow.Rank), 3);
        Assert.True(item.ConfirmInsert());
        Assert.True(item.TryGetOriginal(out var original));
        Assert.Equal(42, original!.Id);
        Assert.Equal("Edited", original.Name);
        Assert.Equal(3, original.Rank);
        item.Set(nameof(ConstructedRow.Name), "Updated");
        Assert.True(item.ConfirmEdit());
        Assert.True(item.TryGetOriginal(out var updated));
        Assert.Same(original, updated);
        Assert.Equal("Updated", original.Name);
    }

    [Fact]
    public void Missing_construction_policy_keeps_the_unconfirmed_edit_buffer() {
        var item = RuntimeTracking.Default<Row>().CreateNew();
        item.Set(nameof(Row.Name), "Pending");
        Assert.Throws<RinkuBindingException>(() => item.ConfirmInsert());
        Assert.True(item.IsNew);
        Assert.False(item.TryGetOriginal(out _));
        Assert.Equal("Pending", item.Get<string>(nameof(Row.Name)));
    }

    [Fact]
    public void Insert_outputs_override_snapshot_defaults_for_mutable_members() {
        var item = RuntimeTracking.CreateOptions<MutableRow, ITrackingItem<MutableRow>>().ConstructOnInsert().GetRegistration<ITrackingItem<MutableRow>>().CreateNew();
        item.PendingParameters = new TrackingParameters();
        item.PendingParameters.Set(nameof(MutableRow.Id), 42);
        item.Set(nameof(MutableRow.Name), "Edited");
        Assert.True(item.ConfirmInsert());
        Assert.Equal(42, item.Get<int>(nameof(MutableRow.Id)));
        Assert.Equal("Edited", item.Get<string>(nameof(MutableRow.Name)));
    }

    public sealed class MutableRow {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ConstructedRow(int id) {
        public int Id { get; } = id;
        public string Name { get; set; } = string.Empty;
        public int Rank;
    }

    public class Row
    {
        public string Name { get; set; } = string.Empty;
    }
}
