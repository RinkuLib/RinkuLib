# Editable items and lists

## Editable reference values

`EditableClass<T>` stores an original reference value and creates an edit copy only when requested.

```csharp
public sealed class Playlist {
    public string? Name { get; set; }
}

Playlist playlist = new() { Name = "Original" };
EditableClass<Playlist> item =
    EditableClass<Playlist>.FromOriginal(playlist);

if (item.EnsureIsEditing(out Playlist editable))
    editable.Name = "Updated";

bool dirty = item.HasChanges;
```

Commit the copy as the new original.

```csharp
item.CommitEdit();
```

Cancel the edit when the original value should remain unchanged.

```csharp
item.CancelEdit();
```

Start a newly added value directly in its editable state.

```csharp
EditableClass<Playlist> item =
    EditableClass<Playlist>.CreateNew(new Playlist());
```

`EditableStruct<T>` provides the same behavior for value types. Metadata-carrying forms use `EditableClass<T, TMetadata>` and `EditableStruct<T, TMetadata>`.

## Read the current value

`CurrentValue` returns the edit while editing and the original otherwise.

```csharp
Playlist shown = item.CurrentValue;
```

Use `EditableValue` or `EnsureIsEditing` when the caller needs a value it may change.

## Track a list

`TrackingList` adds removal and revival history.

```csharp
List<Playlist> playlists = [new(), new(), new()];
var list = playlists.ToTrackingList();

list.RemoveAt(2);

IReadOnlyList<Playlist> removed = list.Removed;
bool found = list.HasOriginal(0, out Playlist original);

list.CommitRemoved();
```

`TrackingEditList` combines that removal history with per-item edit state.

```csharp
var list = playlists.ToTrackingList<Playlist, string?>(validator: (playlist, _) => string.IsNullOrWhiteSpace(playlist?.Name) ? "Name is required" : null);

bool editing = list.IsEditing(0);

list.EnsureEditing(0, out Playlist draft);
draft.Name = "Updated";

bool changed = list.HasChanges;

list.CommitEdit(0);
list.CancelEdit(0);
```

The `ToTrackingList` overloads select class or struct items and optional validation, commit, and metadata behavior.

## Validate and commit through a processor

`IEditProcessor<TEdit, TMetadata>` controls validation and commit metadata.

```csharp
readonly struct PlaylistProcessor
    : IEditProcessor<Playlist, string?> {

    public bool DoValidate => true;
    public bool DoCommit => false;

    public string? Validate(Playlist? value, object? context) => string.IsNullOrWhiteSpace(value?.Name)
            ? "Name is required"
            : null;

    public string? Commit(Playlist value) => null;

    public bool IsValid(string? metadata) => metadata is null;
}
```

```csharp
var list = playlists.ToTrackingList<
    Playlist,
    string?,
    PlaylistProcessor>(new PlaylistProcessor());

list.EnsureEditing(0, out Playlist draft);
draft.Name = "";

if (!list.CommitEdit(0))
    Console.WriteLine(list.GetMetadata(0));
```

```text
Name is required
```

`DoValidate` and `DoCommit` let the list skip unused hooks. `Validate` and `Commit` produce metadata, while `IsValid` interprets it.

Validator and committer delegates build a `DelegateEditProcessor`. `NoOpEditProcessor` supplies neither behavior.

## Bind to a UI list

`TrackingEditList` implements `IList<T>`, `IList`, and `IBindingList`.

```csharp
var list = playlists.ToTrackingList<Playlist, string?>(validator: (playlist, _) => null);

dataGridView.DataSource = list;
list.SetNewItemFactory(() => new Playlist());
```

It raises `ListChanged` and supports `AddNew()` after a new-item factory is available.

The `ToTrackingList` overloads configure the factory automatically when the element type has a parameterless constructor.
