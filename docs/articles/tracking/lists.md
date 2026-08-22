# Tracking lists

`TrackingList<T>` tracks structural list changes without requiring editable items.

```csharp
TrackingList<Album> albums = new(existingAlbums);

albums.Add(new Album(0, "New album"));
albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

Use generated edit items when structural changes and member changes are both needed.

```csharp
TrackingList<IRuntimeTrackingItem<Album>> albums = existingAlbums.ToTrackingList();
```

## Add an item

```csharp
IRuntimeTrackingItem<Album> item = albums.AddNew();
item.Set(nameof(Album.Title), "New album");

Console.WriteLine(albums.IsAddedAt(albums.Count - 1));
```

`AddNew()` uses the list context. `CanAddNew` reports whether that context can create an item.

```csharp
if (albums.CanAddNew)
    albums.AddNew();
```

An item created elsewhere can be added to the list.

```csharp
albums.Add(edit);
albums.Insert(0, edit);
```

## Remove an item

```csharp
IRuntimeTrackingItem<Album> removed = albums[0];
albums.RemoveAt(0);

Console.WriteLine(albums.RemovedCount);
```

Removing an accepted item keeps it in `Removed` until deletion is confirmed.

Removing an item that is still new cancels the addition instead of creating a deletion.

## Restore a removal

```csharp
IRuntimeTrackingItem<Album> removed = albums.Removed[0];
albums.Restore(removed);
```

Restore at a specific active index when order matters.

```csharp
albums.RestoreAt(0, 2);
```

## Move an item

```csharp
albums.Move(0, 3);
```

Moving changes active order. It does not turn the moved item into an addition or removal.

## Replace an item

```csharp
albums[2] = replacement;
```

The configured comparer decides whether the replacement represents the same item.

Pass a comparer when application identity differs from normal equality.

```csharp
TrackingList<AlbumRow> rows = new(existingRows, comparer: EqualityComparer<AlbumRow>.Default);
```

## Read structural changes

```csharp
foreach (IRuntimeTrackingItem<Album> item in albums.Added)
    Console.WriteLine(item.Get<string>(nameof(Album.Title)));

foreach (IRuntimeTrackingItem<Album> item in albums.Removed)
    Console.WriteLine(item.Get<int>(nameof(Album.Id)));
```

`HasChanges` on the list reports additions or removals. Member edits belong to the items.

```csharp
bool structural = albums.HasChanges;
bool memberEdit = albums[0].HasChanges();
```

## Confirm one active item

`ConfirmAt()` chooses addition or edit from the current structural state.

```csharp
bool confirmed = albums.ConfirmAt(0);
```

Use the specific operation when the application already knows which operation succeeded.

```csharp
albums.ConfirmAddedAt(0);
albums.ConfirmEditAt(1);
```

## Confirm a deletion

```csharp
IRuntimeTrackingItem<Album> removed = albums.Removed[0];

DeleteAlbum.Execute(cnn, removed);
albums.ConfirmDelete(removed);
```

A confirmed deletion is removed from the `Removed` collection.

## Confirm every observed operation

```csharp
bool allConfirmed = albums.ConfirmChanges();
```

`ConfirmChanges()` asks the list context to confirm every current operation. Use operation specific confirmation when persistence is performed one operation at a time.

See [persistence](persistence.md) for that pattern.

## Use a concrete item type

Automatic conversion from original values is for generated interface contracts. Construct a normal `TrackingList<T>` when the application owns a concrete edit type.

```csharp
IEnumerable<AlbumRow> rows = existingRows;
TrackingList<AlbumRow> tracked = new(rows);
```

A custom `ITrackingListContext<T>` can provide new item creation and confirmation behavior when the concrete type needs it.
