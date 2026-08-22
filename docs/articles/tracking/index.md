# Tracking

Tracking keeps accepted values separate from edits and keeps list membership changes visible until the application accepts them.

```csharp
public record Album(int Id, string Title);

Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

The generated item reports member changes. The original value stays available until the edit is confirmed.

See [editable items](items.md) for edit state and change inspection.

## Track a list

```csharp
List<Album> source = GetAlbums.Query<List<Album>>(cnn);
TrackingList<IRuntimeTrackingItem<Album>> albums = source.ToTrackingList();

IRuntimeTrackingItem<Album> added = albums.AddNew();
added.Set(nameof(Album.Title), "New album");

albums.RemoveAt(0);

Console.WriteLine(albums.AddedCount);
Console.WriteLine(albums.RemovedCount);
```

A `TrackingList<T>` tracks additions, removals, replacements, and order. Generated items can track their member edits at the same time.

See [tracking lists](lists.md) for structural changes and confirmation.

## Choose the edit surface

Use the default runtime item when member names are only known at runtime.

```csharp
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);
edit.Set(nameof(Album.Title), "Kind of Blue");
```

Use an interface contract when normal typed properties are useful.

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);

edit.Title = "Kind of Blue";
```

Use a concrete type directly with `TrackingList<T>` when the application owns that type and its construction.

```csharp
TrackingList<AlbumRow> rows = new(existingRows);
```

See [runtime tracking](runtime.md) for generated contracts and member options.

## Binding

Binding uses the same tracking model and adds binding notifications and binding list behavior.

```csharp
BindingTrackingList<IRuntimeTrackingItem<Album>> albums = source.ToBindingList();
```

See [binding](binding.md) for binding lists and source aware behavior.

## Validation and metadata

Validation and metadata can be added to generated edit types as options.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();

options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));
options.Metadata<Album, string[]>();
```

See [validation and metadata](validation.md) for synchronous validation, asynchronous validation, caller context, and metadata.

## Save changes

Tracking does not write to a database by itself. Persist the operation with application code, then confirm the matching tracked operation after it succeeds.

```csharp
if (edit.HasChanges())
{
    UpdateAlbum.Execute(cnn, edit);
    edit.ConfirmEdit();
}
```

See [persistence](persistence.md) for items, additions, removals, and transactions.
