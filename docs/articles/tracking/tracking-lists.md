# Tracking lists

*Collections that track additions, removals, and edits.*

Two list types build on the [editable items](editable-items.md).

- **`TrackingList<TOg, TTrackingItem>`** tracks removals and revivals. It exposes the removed items, can discard that history with `CommitRemoved()`, and answers `HasOriginal(index, out original)`.
- **`TrackingEditList<TOg, TEdit, TEditItem>`** also tracks per-item **edit state**, with `HasChanges`, `IsEditing(index)`, `EnsureEditing(index[, out edit])`, `CommitEdit(index)`, and `CancelEdit(index, canRemove)`. It implements `IList<T>` and `IBindingList`, so it works with UI data binding. It raises `ListChanged` and supports `AddNew` when a new-item factory is set with `SetNewItemFactory`.

## Creating one

The `ToTrackingList` extensions build an edit list from any sequence. There are overloads for classes (`TrackingExtensions`) and structs (`TrackingExtensionsStruct`), with optional metadata, validation, and commit support.

```csharp
// Simplest, a class edit list
var list = playlists.ToTrackingList();

// With an edit processor that validates or commits and produces metadata
var list2 = playlists.ToTrackingList<Playlist, MyMetadata, MyProcessor>(processor);
```

## Edit processors

`IEditProcessor<TEdit, TMetadata>` plugs validation and commit logic into a list.

- `DoCommit` and `DoValidate` are capability flags. When `false`, the list skips that hook.
- `Validate(value, context)` returns metadata describing the result (the list passes itself as `context`).
- `Commit(value)` returns metadata describing the commit.
- `IsValid(metadata)` says whether the metadata represents a valid result.

`NoOpEditProcessor<TEdit, TMetadata>` is a do-nothing implementation for lists that need no validation or commit hooks. `DelegateEditProcessor<,>` lets you supply the three functions inline (the `ToTrackingList` overloads that take a `validator`, `committer`, or `isValid` delegate build one for you). Lists implementing `IMetadataList<TMetadata>` and `IValidatableList<TError>` expose the resulting metadata and validation state per index, with `GetMetadata(index)`, `Validate(index)`, and `IsValid(index)`.

## A worked example

A custom processor that validates and reports errors as `string`.

```csharp
readonly struct PlaylistProcessor : IEditProcessor<Playlist, string?> {
    public bool DoValidate => true;
    public bool DoCommit   => false;          // commit handled by the item itself

    public string? Validate(Playlist? value, object? context)
        => string.IsNullOrWhiteSpace(value?.Name) ? "Name is required" : null;

    public string? Commit(Playlist value) => null;
    public bool IsValid(string? metadata) => metadata is null;   // null means no error
}

var list = playlists.ToTrackingList<Playlist, string?, PlaylistProcessor>(new PlaylistProcessor());

// edit detection
list.EnsureEditing(0, out var draft);
draft.Name = "";
bool dirty = list.HasChanges;        // true, row 0 is editing

// validation gates the commit
if (!list.CommitEdit(0))             // runs Validate, stores "Name is required", refuses
    Console.WriteLine(list.GetMetadata(0));

// add and remove are tracked
list.Add(new Playlist { Name = "New" }); // ItemAdded raised via IBindingList
list.RemoveAt(2);                        // original of row 2 lands in list.Removed
```

Because `TrackingEditListBase` implements `IList<T>`, `IList`, and `IBindingList`, the same list binds directly to a `DataGridView` or WPF grid. It raises `ListChanged` and supports `AddNew()` once a new-item factory is set (the `ToTrackingList` overloads wire one up when the element type has a parameterless constructor, or you can call `SetNewItemFactory`).
