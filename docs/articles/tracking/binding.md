# Binding

The examples use the `Album`, `IAlbumEdit`, and `existingAlbums` setup from the [tracking overview](index.md).

## Default binding list

```csharp
BindingTrackingList<ITrackingItem<Album>> albums = existingAlbums.ToBindingList();
```

The binding list exposes the same member and structural changes through `IBindingList` notifications.

## Typed contract

```csharp
BindingList<Album> source = new(existingAlbums);
BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>();
albums[0].Title = "Kind of Blue";
```

## BindingList source

```csharp
BindingList<Album> source = new(existingAlbums);
BindingTrackingList<ITrackingItem<Album>> albums = source.ToBindingList();
```

When the source implements `IList<TOriginal>`, successful confirmations are applied back to that source.

The materialization overload attaches BindBack automatically for an `IList<TOriginal>` source. The same behavior can be attached explicitly and detached through its disposable subscription.

```csharp
RuntimeTrackingRegistration<Album, IAlbumEdit> registration = RuntimeTracking.CreateOptions<Album, IAlbumEdit>().GetRegistration<IAlbumEdit>();
TrackingList<IAlbumEdit> edits = new(source.Select(registration.Create));
using IDisposable binding = edits.BindBack(source);
```

## Custom runtime options

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Member<int>(nameof(Album.Id)).ReadOnly();

BindingTrackingList<IAlbumEdit> albums = source.ToBindingList<Album, IAlbumEdit>(options);
```

The supplied options remain reusable without binding-specific changes.

## Asynchronous loading

An async stream can be materialized directly into its final tracking storage:

```csharp
BindingTrackingList<IAlbumEdit> albums = await originals.ToBindingListAsync<Album, IAlbumEdit>(options, cancellationToken);
```

No intermediate original list is required. Failed or cancelled materialization disposes the incomplete tracking list. `ToTrackingListAsync` provides the same operation without requiring component-model binding.

## Structural operations

```csharp
IAlbumEdit added = albums.AddNew();
added.Title = "New album";

albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

[Tracking lists](lists.md)

`BindBack` consumes `ITrackingConfirmations<out T>`, which exposes read-only items and accepted-operation subscriptions. Implementations do not need to inherit `TrackingList<T>`.
