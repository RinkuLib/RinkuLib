# Tracking lists

```csharp
public record Album(int Id, string Title);

List<Album> existingAlbums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");
static readonly QueryCommand DeleteAlbum = new("DELETE FROM albums WHERE AlbumId = @Id");
```

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
TrackingList<IRuntimeTrackingItem<Album>> albums = existingAlbums.ToTrackingList();

albums[0].Set(nameof(Album.Title), "Kind of Blue");
```

Member changes belong to the items. Structural changes belong to the list.

```csharp
bool structural = albums.HasChanges;
bool memberEdit = albums[0].HasChanges();
```

## AddNew

```csharp
IRuntimeTrackingItem<Album> item = albums.AddNew();
item.Set(nameof(Album.Title), "New album");

Console.WriteLine(albums.IsAddedAt(albums.Count - 1));
```

```csharp
if (albums.CanAddNew)
    albums.AddNew();
```

An already created item can also be inserted.

```csharp
albums.Add(edit);
albums.Insert(0, edit);
```

## Remove

```csharp
IRuntimeTrackingItem<Album> removed = albums[0];
albums.RemoveAt(0);

Console.WriteLine(albums.RemovedCount);
```

An accepted removed item remains in `Removed` until deletion is confirmed. Removing an item that is still a new addition cancels that addition instead.

## Restore

```csharp
IRuntimeTrackingItem<Album> removed = albums.Removed[0];
albums.Restore(removed);
```

```csharp
albums.RestoreAt(0, 2);
```

## Move

```csharp
albums.Move(0, 3);
```

Moving changes active order without creating an addition or removal.

## Replace

```csharp
albums[2] = replacement;
```

The configured comparer determines whether the replacement represents the same item.

```csharp
TrackingList<AlbumRow> rows = new(existingRows, comparer: EqualityComparer<AlbumRow>.Default);
```

## Enumerate structural changes

```csharp
foreach (IRuntimeTrackingItem<Album> item in albums.Added)
    Console.WriteLine(item.Get<string>(nameof(Album.Title)));

foreach (IRuntimeTrackingItem<Album> item in albums.Removed)
    Console.WriteLine(item.Get<int>(nameof(Album.Id)));
```

## Confirm one active operation

```csharp
bool confirmed = albums.ConfirmAt(0);
```

The specific operations are also exposed.

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

## Confirm current operations

```csharp
bool allConfirmed = albums.ConfirmChanges();
```

[Persistence](persistence.md)

## Concrete item type

```csharp
IEnumerable<AlbumRow> rows = existingRows;
TrackingList<AlbumRow> tracked = new(rows);
```

Application-defined list behavior can be supplied through <xref:Rinku.Tracking.ITrackingListContext`1>.
