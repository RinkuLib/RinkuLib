# Binding

A binding tracking list adds binding behavior to the same generated edit and structural tracking model.

```csharp
using Rinku.Tracking.Binding;

List<Album> source = GetAlbums.Query<List<Album>>(cnn);
BindingTrackingList<IRuntimeTrackingItem<Album>> albums = source.ToBindingList();
```

The generated CLR items expose bindable properties even when the caller holds them through the runtime tracking interface.

## Use a typed edit contract

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    int Id { get; }
    string Title { get; set; }
}

BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>();

albums[0].Title = "Kind of Blue";
```

Use a typed contract when the UI or application code can work with normal properties.

## Use a BindingList source

A `BindingList<TOriginal>` can be used as the original source.

```csharp
BindingList<Album> source = new(existingAlbums);
BindingTrackingList<IRuntimeTrackingItem<Album>> albums = source.ToBindingList();
```

The generated list uses a source aware tracking context for an `IList<TOriginal>` source.

## Use custom runtime options

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Member<int>(nameof(Album.Id)).ReadOnly();

BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>(options);
```

Binding materialization applies binding support to a copy of the supplied runtime options. The caller can keep the original option set for nonbinding materialization.

## Add and remove items

Binding lists keep the same structural tracking operations.

```csharp
IAlbumEdit added = albums.AddNew();
added.Title = "New album";

albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

See [tracking lists](lists.md) for confirmation, restore, move, and comparer behavior.
