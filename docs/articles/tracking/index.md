# Tracking

## One editable item

```csharp
public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

`GetChanges()` enumerates the differences that exist now between accepted and edited values.

[Editable items](items.md)

## Typed contract

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    int Id { get; }
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);

edit.Title = "Kind of Blue";
```

The generated CLR type implements the contract while the same runtime tracking state remains available through `IRuntimeTrackingItem<Album>`.

[Runtime tracking](runtime.md)

## Structural list changes

```csharp
List<Album> existingAlbums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");
TrackingList<Album> albums = new(existingAlbums);

albums.Add(new Album(0, "New album"));
albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

The list can also contain generated editable items.

```csharp
TrackingList<IRuntimeTrackingItem<Album>> albums = existingAlbums.ToTrackingList();

IRuntimeTrackingItem<Album> added = albums.AddNew();
added.Set(nameof(Album.Title), "New album");
```

[Tracking lists](lists.md)

## Binding

```csharp
using Rinku.Tracking.Binding;

BindingTrackingList<IRuntimeTrackingItem<Album>> albums = existingAlbums.ToBindingList();
```

[Binding](binding.md)

## Validation and metadata

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();

options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));
options.Metadata<Album, string[]>();
```

[Validation and metadata](validation.md)

## Persist and confirm

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

if (edit.HasChanges())
{
    UpdateAlbum.Execute(cnn, edit);
    edit.ConfirmEdit();
}
```

[Persistence](persistence.md)
