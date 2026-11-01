# Tracking

The examples assume `cnn` is an open `DbConnection` and use this model and generated edit contract:

```csharp
using Rinku;
using Rinku.Tracking;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Committers;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

public interface IAlbumEdit : ITrackingItem<Album>
{
    int Id { get; set; }
    string Title { get; set; }
}

Album original = new(12, "Blue");
List<Album> existingAlbums = [new(12, "Blue"), new(13, "Green")];
```

## One editable item

```csharp
ITrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

`GetChanges()` enumerates the differences that exist now between accepted and edited values.

[Editable items](items.md)

## Typed contract

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);

edit.Title = "Kind of Blue";
```

The returned item implements the contract and remains available through `ITrackingItem<Album>`.

[Runtime tracking](runtime.md)

## Structural list changes

```csharp
TrackingList<Album> albums = new(existingAlbums);

albums.Add(new Album(0, "New album"));
albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

The list can also contain generated editable items.

```csharp
TrackingList<ITrackingItem<Album>> albums = existingAlbums.ToTrackingList();

ITrackingItem<Album> added = albums.AddNew();
added.Set(nameof(Album.Title), "New album");
```

[Tracking lists](lists.md)

## Binding

```csharp
BindingTrackingList<ITrackingItem<Album>> albums = existingAlbums.ToBindingList();
```

[Binding](binding.md)

## Validation

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();

options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));
options.Member<string[]>("Validation", member => member.Direct().Parameters(false));
```

[Validation](validation.md)

## Persist and confirm

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");
static readonly QueryCommitter SaveAlbumChanges = Committer.Create(update: UpdateAlbum);

bool committed = SaveAlbumChanges.Commit(cnn, albums);
```

[Persistence](persistence.md)
