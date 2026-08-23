# Persistence

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand InsertAlbum = new("INSERT INTO albums (Title) VALUES (@Title)");
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");
static readonly QueryCommand DeleteAlbum = new("DELETE FROM albums WHERE AlbumId = @Id");

Album original = new(12, "Blue");
List<Album> source = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");
TrackingList<IRuntimeTrackingItem<Album>> albums = source.ToTrackingList();
```

Tracking reports current operations. Database commands remain application code.

## Edited item

```csharp
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);
edit.Set(nameof(Album.Title), "Kind of Blue");

if (edit.HasChanges())
{
    UpdateAlbum.Execute(cnn, edit);
    edit.ConfirmEdit();
}
```

The generated tracking item can supply its projected members as query values.

[Runtime query projection](runtime.md#query-parameter-projection)

## Cancel

```csharp
edit.CancelEdit();
```

Cancel changes only tracking state. It does not execute a database command.

## Added item

```csharp
IRuntimeTrackingItem<Album> added = albums.Added.First();

InsertAlbum.Execute(cnn, added);
albums.Confirm(added);
```

## Removed item

```csharp
IRuntimeTrackingItem<Album> removed = albums.Removed.First();

DeleteAlbum.Execute(cnn, removed);
albums.ConfirmDelete(removed);
```

## Edited existing items

```csharp
for (int i = 0; i < albums.Count; i++)
{
    IRuntimeTrackingItem<Album> item = albums[i];

    if (albums.IsAddedAt(i) || !item.HasChanges())
        continue;

    UpdateAlbum.Execute(cnn, item);
    albums.ConfirmEditAt(i);
}
```

## Transaction

```csharp
using DbTransaction tx = cnn.BeginTransaction();

foreach (IRuntimeTrackingItem<Album> item in albums.Added)
    InsertAlbum.Execute(cnn, item, transaction: tx);

foreach (IRuntimeTrackingItem<Album> item in albums.Removed)
    DeleteAlbum.Execute(cnn, item, transaction: tx);

for (int i = 0; i < albums.Count; i++)
{
    IRuntimeTrackingItem<Album> item = albums[i];

    if (albums.IsAddedAt(i) || !item.HasChanges())
        continue;

    UpdateAlbum.Execute(cnn, item, transaction: tx);
}

tx.Commit();
albums.ConfirmChanges();
```

Tracked state is confirmed after the transaction commits.

[Transactions](../running-queries/execution-context.md) · [Tracking lists](lists.md)
