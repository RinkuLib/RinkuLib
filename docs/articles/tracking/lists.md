# Tracking lists

The examples use the `existingAlbums` and `ITrackingItem<Album>` setup from the [tracking overview](index.md).

## Structural tracking with a concrete type

```csharp
TrackingList<Album> albums = new(existingAlbums);

albums.Add(new Album(0, "New album"));
albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

`TrackingList<T>` tracks active membership and structural changes without requiring editable items.

## Structural and member tracking together

```csharp
TrackingList<ITrackingItem<Album>> editableAlbums = existingAlbums.ToTrackingList();

editableAlbums[0].Set(nameof(Album.Title), "Kind of Blue");
```

Member changes belong to the items. Structural changes belong to the list, and the list exposes both as pending work.

`RevertChanges()` discards pending item edits, unconfirmed additions and pending removals. It does not undo confirmed changes or restore an earlier ordering: surviving items keep their current order, and restored removals are appended. Revert uses the list's own state; an external original collection or bind-back callback is not required.

```csharp
bool pending = editableAlbums.HasChanges;

foreach ((ITrackingItem<Album> item, bool isInsert) in editableAlbums.GetChanged())
    Console.WriteLine(isInsert ? "Insert" : "Update");
```

## AddNew

```csharp
ITrackingItem<Album> item = editableAlbums.AddNew();
item.Set(nameof(Album.Title), "New album");

Console.WriteLine(editableAlbums.IsAddedAt(editableAlbums.Count - 1));

if (editableAlbums.CanAddNew)
    editableAlbums.AddNew();
```

An already created item can also be inserted.

```csharp
ITrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(new Album(15, "Edit"));
editableAlbums.Add(edit);
// Or insert it at a specific index instead:
// editableAlbums.Insert(0, edit);
```

## Remove

```csharp
ITrackingItem<Album> removed = editableAlbums[0];
editableAlbums.RemoveAt(0);

Console.WriteLine(editableAlbums.RemovedCount);
```

An accepted removed item remains in `Removed` until deletion is confirmed. Removing an item that is still a new addition cancels that addition instead.

## Restore

```csharp
ITrackingItem<Album> removed = editableAlbums.Removed[0];
editableAlbums.RestoreAt(removedIndex: 0, index: 1);

// Or restore by item and append it to the active list.
// editableAlbums.Restore(removed);
```

## Move

```csharp
editableAlbums.Move(0, 1);
```

Moving changes active order without creating an addition or removal.

## Replace

```csharp
Album replacement = new(14, "Replacement");
ITrackingItem<Album> replacementItem = RuntimeTracking.Default<Album>().Create(replacement);
editableAlbums[0] = replacementItem;
```

The list comparer determines whether the replacement is equivalent to the previous item. The default comparer compares the item objects; provide a comparer when logical identity should use a different rule.

## Enumerate structural changes

```csharp
foreach (ITrackingItem<Album> item in editableAlbums.Added)
    Console.WriteLine(item.Get<string>(nameof(Album.Title)));

foreach (ITrackingItem<Album> item in editableAlbums.Removed)
    Console.WriteLine(item.Get<int>(nameof(Album.Id)));
```

## Confirm one active operation

```csharp
bool confirmed = editableAlbums.ConfirmAt(0);
```

The specific operations are also exposed.

```csharp
editableAlbums.ConfirmAddedAt(0);
editableAlbums.ConfirmEditAt(1);
```

## Confirm a deletion

```csharp
ITrackingItem<Album> removed = editableAlbums.Removed[0];
editableAlbums.ConfirmDelete(removed);
```

## Confirm current operations

```csharp
bool allConfirmed = editableAlbums.ConfirmChanges();
```

[Persistence](persistence.md)

## Item confirmation capabilities

`TrackingList<T>` confirms item-owned state directly when `T` implements the corresponding capability.

```csharp
public sealed class AlbumRow : ICanInsert, ICanUpdate, ICanDelete
{
    public bool ConfirmInsert() => true;
    public bool ConfirmUpdate() => true;
    public bool ConfirmDelete() => true;
}
```

When a capability is absent, confirmation succeeds without an item-level action. `IEditable` supplies insertion and update confirmation. Deletion remains independent through `ICanDelete`.

## Confirmation subscriptions

Successful confirmations can be observed independently of item confirmation.

```csharp
using IDisposable subscription = editableAlbums.SubscribeConfirmations(onAdded: (index, item) => Console.WriteLine($"Added at {index}"), onUpdated: (index, item) => Console.WriteLine($"Updated at {index}"), onDeleted: item => Console.WriteLine("Deleted"));
```

Added and updated callbacks receive the active index. Deleted callbacks receive only the deleted item. Callbacks run after the operation is confirmed.
