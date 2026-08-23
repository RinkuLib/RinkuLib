# Validation and metadata

## Validation

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
bool valid = ((IValidatable)edit).Validate();
```

## Validation with caller context

```csharp
options.Validate<Album, IAlbumEdit, HashSet<string>>(
    static (edit, reservedTitles) => !reservedTitles.Contains(edit.Title));

IValidatable<HashSet<string>> validatable = (IValidatable<HashSet<string>>)edit;
bool valid = validatable.Validate(reservedTitles);
```

The context type is supplied by application code at validation time.

## Async validation

```csharp
options.ValidateAsync<Album, IAlbumEdit>(
    static (edit, cancellationToken) => ValueTask.FromResult(!string.IsNullOrWhiteSpace(edit.Title)));

IAsyncValidatable validatable = (IAsyncValidatable)edit;
bool valid = await validatable.ValidateAsync(cancellationToken);
```

## Async validation with context

```csharp
options.ValidateAsync<Album, IAlbumEdit, HashSet<string>>(
    static (edit, reservedTitles, cancellationToken) => ValueTask.FromResult(!reservedTitles.Contains(edit.Title)));

IAsyncValidatable<HashSet<string>> validatable = (IAsyncValidatable<HashSet<string>>)edit;
bool valid = await validatable.ValidateAsync(reservedTitles, cancellationToken);
```

The context object is supplied by the caller.

## Metadata

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Metadata<Album, string[]>();

IRuntimeTrackingItem<Album> edit = options.GetRegistration<IRuntimeTrackingItem<Album>>().Create(original);

((IMetadataWriter<string[]>)edit).SetMetadata(["Title is required"]);
string[] messages = ((IMetadataReader<string[]>)edit).Metadata;
```

Reader and writer support can be configured separately.

```csharp
options.Metadata<Album, string[]>(reader: true, writer: false);
```

The metadata type and its meaning belong to application code.
