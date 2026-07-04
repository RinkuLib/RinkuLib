# Items and lists

## Editable items

An editable item holds an original value and a lazily created edit copy. `EditableClass<T>` for reference types, `EditableStruct<T>` for value types.

```csharp
var item  = EditableClass<Playlist>.FromOriginal(playlist);   // start from an existing value
var blank = EditableClass<Playlist>.CreateNew(newPlaylist);   // start directly in the edit state

if (item.EnsureIsEditing(out Playlist editable))
    editable.Name = "Updated";

bool dirty = item.HasChanges;
item.CommitEdit();   // the edit becomes the new original
item.CancelEdit();   // discard the edit, keep the original
```

`CurrentValue` returns the value as it stands, the edit while editing, otherwise the original, with no mutability guarantee. Use `EditableValue` or `EnsureIsEditing` when you intend to modify.

Metadata-carrying variants exist for validation results: `EditableClass<T, TMetadata>` and `EditableStruct<T, TMetadata>`.

## Tracking lists

Two list types build on the items.

- `TrackingList` tracks removals and revivals. Removed originals are exposed on `Removed`, `CommitRemoved()` discards that history, `HasOriginal(index, out original)` answers per row.
- `TrackingEditList` adds per-item edit state: `HasChanges`, `IsEditing(index)`, `EnsureEditing(index, out edit)`, `CommitEdit(index)`, `CancelEdit(index)`.

The `ToTrackingList` extensions build one from any sequence, with overloads for classes and structs, and optional validation, commit, and metadata.

```csharp
var simple = playlists.ToTrackingList();

var validated = playlists.ToTrackingList<Playlist, string?>(
    validator: (p, _) => string.IsNullOrWhiteSpace(p?.Name) ? "Name is required" : null);
```

## Edit processors

`IEditProcessor<TEdit, TMetadata>` plugs validation and commit logic into a list.

- `DoValidate` and `DoCommit` are capability flags. When false, the list skips that hook.
- `Validate(value, context)` returns metadata describing the result. The list passes itself as context.
- `Commit(value)` returns metadata describing the commit.
- `IsValid(metadata)` interprets the metadata.

The `validator` and `committer` delegates above build one for you (`DelegateEditProcessor`). `NoOpEditProcessor` is the do-nothing implementation. A hand-written one:

```csharp
readonly struct PlaylistProcessor : IEditProcessor<Playlist, string?> {
    public bool DoValidate => true;
    public bool DoCommit   => false;

    public string? Validate(Playlist? value, object? context)
        => string.IsNullOrWhiteSpace(value?.Name) ? "Name is required" : null;

    public string? Commit(Playlist value) => null;
    public bool IsValid(string? metadata) => metadata is null;
}

var list = playlists.ToTrackingList<Playlist, string?, PlaylistProcessor>(new PlaylistProcessor());

list.EnsureEditing(0, out var draft);
draft.Name = "";
if (!list.CommitEdit(0))                    // Validate runs, refuses, stores the metadata
    Console.WriteLine(list.GetMetadata(0)); // "Name is required"
```

## UI binding

`TrackingEditList` implements `IList<T>`, `IList`, and `IBindingList`, so it binds directly to a `DataGridView` or a WPF grid. It raises `ListChanged` and supports `AddNew()` once a new-item factory exists: the `ToTrackingList` overloads wire one up when the element type has a parameterless constructor, or set it with `SetNewItemFactory`.
