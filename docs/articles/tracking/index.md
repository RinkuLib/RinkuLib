# Tracking

`RinkuLib.Tracking` wraps an ordinary `IEnumerable<T>` with change tracking, what was added, removed, or edited, with commit and revert. The result still binds and enumerates like a normal list, which is what a form or a data grid needs.

> **Status.** Tracking is in active development. The blocks below work, but the surface may still change.

Each item holds an **original** value and, once you start editing, a separate **edit** copy. Nothing is copied until you actually edit. Commit makes the edit the new original, cancel discards it.

## End to end

```csharp
public class Playlist { public int Id { get; set; } public string? Name { get; set; } }

var playlists = LoadPlaylists();
var list = playlists.ToTrackingList<Playlist, string?>(
    validator: (p, _) => string.IsNullOrWhiteSpace(p?.Name) ? "Name is required" : null);

// edit: a lazy copy is made, the original stays untouched
list.EnsureEditing(0, out Playlist draft);
draft.Name = "Renamed";

if (list.Validate(0)) {
    list.CommitEdit(0);                     // the edit becomes the new original
}
else {
    string? error = list.GetMetadata(0);    // "Name is required"
    list.CancelEdit(0);                     // discard, keep the original
}

// removals are tracked too
list.RemoveAt(3);
IReadOnlyList<Playlist> removed = list.Removed;
```

## The pieces

- [Items and lists](items-and-lists.md). `EditableClass<T>`, `EditableStruct<T>`, the tracking lists, edit processors, UI binding.
- [Copying](copying.md). The fast clones the originals rely on, and how to steer them per field.
