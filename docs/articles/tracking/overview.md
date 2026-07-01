# Tracking

*Edit, commit, and revert over your collections.*

`RinkuLib.Tracking` is a layer over an ordinary `IEnumerable<T>`. Wrap a sequence and you get change tracking on top of it, what was added, removed, or edited, so you can commit or roll back. It's the kind of thing a form or a data grid needs, and because it builds on `IEnumerable<T>`, the result still binds and enumerates like a normal list.

> **Status.** Tracking is still in active development. The building blocks below work, but the feature is not finished and parts of the surface may still change. Use it with that in mind.

It has three building blocks.

- **Editable items** ([editable-items.md](editable-items.md)) wrap a value with a lazy editable copy, so you can edit freely and still recover the original.
- **Tracking lists** ([tracking-lists.md](tracking-lists.md)) track additions, removals, and per-item edit state across a collection, with optional metadata and validation.
- **Copying** ([copying.md](copying.md)) makes independent snapshots with fast, IL-generated clones.

## The original-versus-edited model

An editable item holds an **original** value and, once you start editing, a separate **edit** value. You can check whether it has changes, commit the edit (the edit becomes the new original), or cancel it (the edit is discarded). Nothing is copied until you actually begin editing.

## End to end

Load a collection, edit one row, validate, then commit or revert, without touching the originals until you choose to.

```csharp
// a mutable type to edit. Tracking works over any IEnumerable<T>
public class Playlist { public int Id { get; set; } public string? Name { get; set; } }

// wrap a sequence in a tracking edit list (validation optional)
var playlists = LoadPlaylists();
var list = playlists.ToTrackingList<Playlist, string?>(
    validator: (p, _) => string.IsNullOrWhiteSpace(p?.Name) ? "Name is required" : null);

// edit, a lazy copy is made the first time. The original is untouched
list.EnsureEditing(0, out Playlist draft);
draft.Name = "Renamed";

// validate, runs the processor and stashes the metadata for that row
if (list.Validate(0)) {
    list.CommitEdit(0);     // the edit becomes the new original
}
else {
    string? error = list.GetMetadata(0);   // "Name is required"
    list.CancelEdit(0);     // discard the edit, keep the original
}

// removals are tracked too
list.RemoveAt(3);
IReadOnlyList<Playlist> removed = list.Removed;   // the original of row 3
```

See [editable items](editable-items.md) for the per-item mechanics and [tracking lists](tracking-lists.md) for the collection API and edit processors.
