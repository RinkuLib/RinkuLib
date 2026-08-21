# Tracking items

A generated tracking item separates its accepted original value from a lazy edit snapshot.

## Runtime-generated item

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
bool editing = edit.IsEditing;
```

The accepted original remains available independently from edit state and new-item state.

```csharp
if (edit.TryGetOriginal(out Album? accepted))
    Console.WriteLine(accepted.Title);
```

## Edit lifecycle

`IEditable` exposes `IsEditing`, `EnsureEditing`, `ConfirmEdit`, and `CancelEdit`.

```csharp
edit.EnsureEditing();
edit.Set(nameof(Album.Title), "Kind of Blue");

edit.ConfirmEdit();
// or edit.CancelEdit();
```

`ConfirmEdit` accepts the current snapshot into the item's original; it does not acknowledge that a new row was inserted. `CancelEdit` discards the snapshot and leaves the accepted original unchanged.

## Application-owned concrete types

Automatic original-to-edit materialization is intentionally interface-only. When an application owns a concrete edit type, construct it directly and place it in `TrackingList<T>` with an appropriate `ITrackingListContext<T>` when confirmation or new-item creation requires custom behavior.
