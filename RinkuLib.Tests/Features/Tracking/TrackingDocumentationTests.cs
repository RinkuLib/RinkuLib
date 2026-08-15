using System.ComponentModel;
using Rinku.Tracking;
using Xunit;

namespace RinkuLib.Tests.Tracking;

/// <summary>Executable coverage for the examples in the tracking documentation.</summary>
public class TrackingDocumentationTests {
    [Fact]
    public void Editable_class_keeps_the_original_until_commit_or_cancel() {
        var original = new Playlist(1, "Original");
        var item = EditableClass<Playlist>.FromOriginal(original);

        Assert.False(item.IsEditing);
        Assert.True(item.EnsureIsEditing(out var draft));
        Assert.NotSame(original, draft);
        draft.Name = "Updated";
        Assert.Equal("Original", original.Name);
        Assert.Equal("Updated", item.CurrentValue!.Name);

        Assert.True(item.CancelEdit());
        Assert.Equal("Original", item.CurrentValue!.Name);
        Assert.True(item.EnsureIsEditing(out draft));
        draft.Name = "Committed";
        Assert.True(item.CommitEdit());
        Assert.Equal("Committed", item.CurrentValue!.Name);
        Assert.False(item.IsEditing);
    }

    [Fact]
    public void Tracking_list_validates_edits_and_tracks_removed_originals() {
        var source = new[] { new Playlist(1, "P1"), new Playlist(2, "P2") };
        var list = source.ToTrackingList<Playlist, string?>(
            validator: (p, _) => string.IsNullOrWhiteSpace(p?.Name) ? "Name is required" : null);
        var validated = Assert.IsAssignableFrom<IValidatableEditableList<Playlist, string?>>(list);

        Assert.Equal(2, list.Count);
        Assert.Equal("P1", list[0].Name);
        Assert.Equal("P2", list[1].Name);
        Assert.True(validated.EnsureEditing(0, out var draft));
        draft.Name = "";
        Assert.False(validated.Validate(0));
        Assert.Equal("Name is required", validated.GetMetadata(0));
        Assert.False(validated.CommitEdit(0));

        Assert.True(validated.CancelEdit(0));
        Assert.True(list.HasOriginal(0, out var original));
        Assert.Same(source[0], original);
        list.RemoveAt(1);
        Assert.Equal([source[1]], list.Removed);
        Assert.Single(list);
        list.Add(source[1]); // Reattaches the removed original instead of creating a duplicate.
        Assert.Equal(2, list.Count);
        Assert.Empty(list.Removed);
        list.RemoveAt(1);
        list.CommitRemoved();
        Assert.Empty(list.Removed);
    }

    [Fact]
    public void Editable_struct_and_new_items_follow_the_same_contract() {
        var item = EditableStruct<int>.FromOriginal(7);
        Assert.True(item.EnsureIsEditing(out var edit));
        Assert.Equal(7, edit);
        Assert.True(item.CancelEdit());
        Assert.Equal(7, item.CurrentValue);

        var blank = EditableClass<Playlist>.CreateNew(new Playlist(3, "New"));
        Assert.True(blank.IsEditing);
        Assert.False(blank.HasOriginal(out _));
        Assert.False(blank.CancelEdit());
    }

    [Fact]
    public void Hand_written_edit_processor_controls_validation_and_commit() {
        var source = new[] { new Playlist(1, "P1") };
        var list = source.ToTrackingList<Playlist, string?, PlaylistProcessor>(new PlaylistProcessor());

        Assert.True(list.EnsureEditing(0, out var draft));
        draft.Name = "";
        Assert.False(list.CommitEdit(0));
        Assert.Equal("Name is required", list.GetMetadata(0));

        draft.Name = "P1 updated";
        Assert.True(list.CommitEdit(0));
        Assert.Equal("P1 updated", list[0].Name);
    }

    [Fact]
    public void Copy_examples_keep_shallow_members_and_clone_marked_members() {
        var invoice = new Invoice {
            Number = "A-1",
            Customer = new Customer("Ada"),
            Lines = [new InvoiceLine("Bolt")],
            BillTo = new Address("Toronto"),
        };

        var snapshot = invoice.Copy()!;

        Assert.NotSame(invoice, snapshot);
        Assert.Equal("A-1", snapshot.Number);
        Assert.Same(invoice.Customer, snapshot.Customer);
        Assert.NotSame(invoice.Lines, snapshot.Lines);
        Assert.NotSame(invoice.Lines[0], snapshot.Lines[0]);
        Assert.NotSame(invoice.BillTo, snapshot.BillTo);
    }

    [Fact]
    public void Runtime_field_plans_work_for_a_type_without_tracking_attributes() {
        var source = new ExternalInvoice { Address = new Address("Toronto") };
        try {
            Copier<ExternalInvoice>.RegisterFieldPlan(
                typeof(ExternalInvoice).GetField(nameof(ExternalInvoice.Address))!, new CopyAttribute());

            var copy = source.Copy()!;
            Assert.NotSame(source, copy);
            Assert.NotSame(source.Address, copy.Address);
            Assert.Equal(source.Address.City, copy.Address.City);
        }
        finally {
            Copier<ExternalInvoice>.ResetFieldPlans();
        }
    }

    [Fact]
    public void Tracking_edit_lists_are_binding_lists_and_support_add_new() {
        var list = new[] { new Playlist(1, "P1") }.ToTrackingList<Playlist>();
        var changes = new ListChangedEventArgs(ListChangedType.ItemAdded, -1);
        list.ListChanged += (_, e) => changes = e;

        list.SetNewItemFactory(() => new Playlist(2, "P2"));
        var added = list.AddNew();

        Assert.True(list.AllowNew);
        Assert.Equal("P2", added.Name);
        Assert.Equal(2, list.Count);
        Assert.Equal(ListChangedType.ItemAdded, changes.ListChangedType);
    }
}

public sealed class Playlist(int id, string? name) {
    public int Id { get; } = id;
    public string? Name { get; set; } = name;
}

public readonly struct PlaylistProcessor : IEditProcessor<Playlist, string?> {
    public bool DoValidate => true;
    public bool DoCommit => false;
    public string? Validate(Playlist? value, object? context)
        => string.IsNullOrWhiteSpace(value?.Name) ? "Name is required" : null;
    public string? Commit(Playlist value) => null;
    public bool IsValid(string? metadata) => metadata is null;
}

public sealed class Invoice {
    public string Number = "";
    public Customer Customer = null!;
    [DeepCollection] public List<InvoiceLine> Lines = [];
    [Copy] public Address BillTo = null!;
}

public sealed class ExternalInvoice {
    public Address Address = null!;
}

public sealed record Customer(string Name);
public sealed record InvoiceLine(string Description);
public sealed record Address(string City);
