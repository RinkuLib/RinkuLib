# Binding

```csharp
using Rinku.Tracking.Binding;

public record Album(int Id, string Title);

List<Album> source = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");
BindingTrackingList<IRuntimeTrackingItem<Album>> albums = source.ToBindingList();
```

The binding list adds binding behavior around the same generated edit and structural tracking state.

## Typed contract

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    int Id { get; }
    string Title { get; set; }
}

BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>();
albums[0].Title = "Kind of Blue";
```

## BindingList source

```csharp
BindingList<Album> source = new(existingAlbums);
BindingTrackingList<IRuntimeTrackingItem<Album>> albums = source.ToBindingList();
```

An `IList<TOriginal>` source uses a source-aware tracking context.

## Custom runtime options

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Member<int>(nameof(Album.Id)).ReadOnly();

BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>(options);
```

Binding materialization applies binding support to a copy of the supplied runtime options.

## Structural operations

```csharp
IAlbumEdit added = albums.AddNew();
added.Title = "New album";

albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

[Tracking lists](lists.md)
