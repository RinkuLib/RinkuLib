# Runtime tracking

Runtime tracking creates an editable implementation from an original type and an interface contract.

```csharp
public record Album(int Id, string Title);

public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    int Id { get; }
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
RuntimeTrackingRegistration<Album, IAlbumEdit> registration = options.GetRegistration<IAlbumEdit>();

IAlbumEdit edit = registration.Create(new Album(12, "Blue"));
edit.Title = "Kind of Blue";
```

The generated CLR type implements the interface. The caller can keep using the interface.

## Use the default contract

Use `IRuntimeTrackingItem<TOriginal>` when compile time properties are not needed.

```csharp
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
```

The default registration is shared for the original type.

## Create custom options

Create a separate option set when generated members or features need configuration.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();

options.Member<string>(nameof(Album.Title));

IRuntimeTrackingItem<Album> edit =
    options.GetRegistration<IRuntimeTrackingItem<Album>>().Create(original);
```

Options become frozen when the first registration is created. Configure them before calling `GetRegistration()`.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>(nameof(Album.Title)).ReadOnly();

RuntimeTrackingRegistration<Album, IRuntimeTrackingItem<Album>> registration =
    options.GetRegistration<IRuntimeTrackingItem<Album>>();
```

## Configure one member

Member options can change the generated surface.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();

options.Member<int>(nameof(Album.Id)).ReadOnly();
options.Member<string>(nameof(Album.Title)).Expose();
```

Use `Ignore()` when the member should not be generated.

```csharp
options.Member<string>("DisplayText").Ignore();
```

Use `RuntimeAccess(false)` when the member can exist on the generated CLR type but should not be available through `Get`, `Set`, or the runtime name map.

```csharp
options.Member<string>("InternalText").RuntimeAccess(false);
```

Use `Parameters(false)` when the member should not be projected as a query parameter.

```csharp
options.Member<string>("DisplayText").Parameters(false);
```

## Use generated edits as query values

Generated runtime items can provide their projected members to a query.

```csharp
static readonly QueryCommand UpdateAlbum = new(
    "UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

IAlbumEdit edit = registration.Create(original);
edit.Title = "Kind of Blue";

UpdateAlbum.Execute(cnn, edit);
```

Member parameter projection can be changed with `Parameters()` when the query surface should differ from the edit surface.

## Create a new item

A registration can create new edits when the original type has an available new value factory.

```csharp
RuntimeTrackingOptions<AlbumDraft> options = RuntimeTracking.CreateOptions<AlbumDraft>();
options.WithNewOriginal(static () => new AlbumDraft());

RuntimeTrackingRegistration<AlbumDraft, IRuntimeTrackingItem<AlbumDraft>> registration =
    options.GetRegistration<IRuntimeTrackingItem<AlbumDraft>>();

IRuntimeTrackingItem<AlbumDraft> edit = registration.CreateNew();
```

`CanCreateNew` reports whether the registration can create a new item.

```csharp
if (registration.CanCreateNew)
    registration.CreateNew();
```

## Treat null as a missing original

Reference originals can treat a null value as unavailable.

```csharp
RuntimeTrackingOptions<Album?> options = RuntimeTracking.CreateOptions<Album?>();
options.UseNullAsMissingOriginal();
```

`TryGetOriginal()` then reports whether an accepted original is available.

## Edit a nested object in place

Use nested editing when a nested object should have its own tracked member changes.

```csharp
public record Artist(int Id, string Name);
public record Album(int Id, string Title, Artist Artist);

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<Artist>(nameof(Album.Artist)).NestedEdit(NestedEditMode.InPlace);
```

`InPlace` accepts nested changes by copying changed members into the accepted nested object.

## Replace a nested object

Use replacement when confirming the nested edit should replace the accepted nested value.

```csharp
options.Member<Artist>(nameof(Album.Artist)).NestedEdit(NestedEditMode.Replacement);
```

The two modes change how an accepted nested edit is applied. The nested edit itself remains tracked.

## Materialize a list with a contract

```csharp
List<Album> source = GetAlbums.Query<List<Album>>(cnn);

TrackingList<IAlbumEdit> albums = source.ToTrackingList<Album, IAlbumEdit>();

albums[0].Title = "Kind of Blue";
```

Pass custom options when the list needs the configured contract.

```csharp
TrackingList<IAlbumEdit> albums = source.ToTrackingList<Album, IAlbumEdit>(options);
```

See [tracking lists](lists.md) for structural changes and [validation and metadata](validation.md) for additional generated capabilities.
