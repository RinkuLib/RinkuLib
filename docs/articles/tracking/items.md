# Tracking items

A tracking item separates the accepted original value from the current editable state.

## Runtime generated item

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeDynamicTrackingItem<Album> edit = original.ToTrackingItem();

edit.Set(nameof(Album.Title), "Kind of Blue");
bool editing = edit.IsEditing;
```

The original remains available through the tracking contract.

```csharp
if (edit.TryGetOriginal(out Album original))
    Console.WriteLine(original.Title);
```

## Edit lifecycle

`IEditable` exposes `IsEditing`, `EnsureEditing`, `CommitEdit`, and `CancelEdit`. `IEditableTrackingItem<TOriginal>` combines that lifecycle with typed original access.

```csharp
edit.EnsureEditing();
edit.Set(nameof(Album.Title), "Kind of Blue");

edit.CommitEdit();
// or edit.CancelEdit();
```

`CommitEdit` accepts the current edit. `CancelEdit` drops the current edit and keeps the accepted original.

## Handwritten edit types

`ToTrackingItem<TOriginal, TEdit>()` also supports a handwritten materialization contract through `IFromOriginal<TOriginal, TEdit>`. A selector overload is available when creation is owned by application code.

Use runtime generation when the edit shape should be generated from the original type, and a handwritten type when the application needs complete control over the edit implementation.
