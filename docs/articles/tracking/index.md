# Tracking

`Rinku.Tracking` keeps editable application state separate from persistence. It can wrap one object, track structural changes in a list, generate runtime edit types, expose original values, and attach validation or metadata capabilities.

## Start with one item

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeDynamicTrackingItem<Album> edit = original.ToTrackingItem();

edit.Set(nameof(Album.Title), "Kind of Blue");
```

The default runtime shape provides edit state, original access, property-change notifications, and member access by name or index.

[Read about tracking items](items.md) and [runtime tracking](runtime.md).

## Track a collection

```csharp
Album[] originals = [new(1, "Blue"), new(2, "Green")];
TrackingList<IRuntimeDynamicTrackingItem<Album>> albums = originals.ToTrackingList();

albums.RemoveAt(0);
bool changed = albums.HasChanges();
```

A `TrackingList<T>` keeps current order, added items, removed items, and item edit state without mixing persistence into the collection.

[Read about tracking lists](lists.md).

## Validation and metadata

Tracking contracts keep validation and metadata independent. Items can expose synchronous validation, asynchronous validation, caller context, metadata reading, metadata writing, or useful combinations of those capabilities.

[Read about validation and metadata](validation.md).

## Persistence stays explicit

Tracking does not execute database operations. Save through the application first, then call `CommitEdit`, `CommitChanges`, or the structural commit operation that matches what was persisted.
