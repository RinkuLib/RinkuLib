# Tracking

`Rinku.Tracking` adds editing, validation, removal history, commit, and revert behavior to an ordinary sequence. Tracking is in active development, so its public surface may still change.

## Track removals

```csharp
List<Playlist> playlists = LoadPlaylists();
var list = playlists.ToTrackingList();

list.RemoveAt(3);

IReadOnlyList<Playlist> removed = list.Removed;
```

The remaining items still enumerate and bind like a normal list.

```csharp
foreach (Playlist playlist in list)
    Show(playlist);
```

Commit the removals after they have been persisted.

```csharp
DeletePlaylists(list.Removed);
list.CommitRemoved();
```

## Edit an item

An item keeps its original value until editing begins. The editable copy is created lazily.

```csharp
public sealed class Playlist {
    public int Id { get; set; }
    public string? Name { get; set; }
}

var list = playlists.ToTrackingList<Playlist, string?>(validator: (playlist, _) => string.IsNullOrWhiteSpace(playlist?.Name) ? "Name is required" : null);

var editable =
    (IValidatableEditableList<Playlist, string?>)list;

editable.EnsureEditing(0, out Playlist draft);
draft.Name = "Renamed";
```

A successful validation can commit the edited value.

```csharp
if (editable.Validate(0))
    editable.CommitEdit(0);
```

Cancel an invalid edit and keep the original.

```csharp
if (!editable.Validate(0)) {
    string? error = editable.GetMetadata(0);
    editable.CancelEdit(0);
}
```

## Commit or revert

```text
original value -> EnsureEditing -> editable copy
editable copy  -> CommitEdit    -> new original
editable copy  -> CancelEdit    -> original remains
removed item   -> CommitRemoved -> removal history cleared
```

The convenience overload assembles editable items, copy behavior, validation, and the tracking list. See [editable items and lists](items-and-lists.md) for the underlying item and processor types.

See [copying](copying.md) for the snapshots used during editing.
