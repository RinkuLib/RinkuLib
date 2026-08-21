# Tracking

`Rinku.Tracking` separates editable application state from persistence. It provides a structural tracking list, cached runtime-generated edit types, source-aware confirmation, binding support, validation, and metadata capabilities.

## Start with one item

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
edit.ConfirmEdit();
```

The generated item exposes a real CLR property surface, lazy edit state, original access, change enumeration, and member access by name or index. Reading does not start an edit; the first write creates a separate snapshot.

[Read about tracking items](items.md) and [runtime tracking](runtime.md).

## Track a collection

```csharp
Album[] originals = [new(1, "Blue"), new(2, "Green")];
TrackingList<IRuntimeTrackingItem<Album>> albums = originals.ToTrackingList();

albums.RemoveAt(0);
bool changed = albums.HasChanges;
```

`TrackingList<T>` owns active membership, order, removed storage, and fallback Added provenance. Generated item capabilities compose through the list context rather than being built into the list.

[Read about tracking lists](lists.md).

## Validation and metadata

Validation and metadata are optional generated capabilities. They remain independent of both `TrackingList<T>` and persistence.

[Read about validation and metadata](validation.md).

## Persistence stays explicit

Tracking never performs database work. After an external operation succeeds, call the matching confirmation method—such as `ConfirmEdit`, `ConfirmAddedAt`, `ConfirmDeleteAt`, or `ConfirmChanges`—so the owner of that state can advance it.
