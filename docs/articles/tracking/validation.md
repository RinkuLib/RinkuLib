# Validation and metadata

Validation can be added to a generated edit contract during runtime tracking configuration.

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();

options.Validate<Album, IAlbumEdit>(
    static edit => !string.IsNullOrWhiteSpace(edit.Title));

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
bool valid = ((IValidatable)edit).Validate();
```

The validation handler receives the generated edit through the contract selected by the application.

## Pass caller context

Use context validation when the rule needs a value supplied by the caller.

```csharp
options.Validate<Album, IAlbumEdit, HashSet<string>>(
    static (edit, reservedTitles) => !reservedTitles.Contains(edit.Title));

IValidatable<HashSet<string>> validatable =
    (IValidatable<HashSet<string>>)edit;

bool valid = validatable.Validate(reservedTitles);
```

The context type belongs to the application.

## Validate asynchronously

```csharp
options.ValidateAsync<Album, IAlbumEdit>(
    static (edit, cancellationToken) =>
        ValueTask.FromResult(!string.IsNullOrWhiteSpace(edit.Title)));

IAsyncValidatable validatable = (IAsyncValidatable)edit;
bool valid = await validatable.ValidateAsync(cancellationToken);
```

The asynchronous handler receives the caller cancellation token.

## Validate asynchronously with context

```csharp
options.ValidateAsync<Album, IAlbumEdit, AlbumRules>(
    static (edit, rules, cancellationToken) =>
        rules.ValidateAsync(edit.Title, cancellationToken));

IAsyncValidatable<AlbumRules> validatable =
    (IAsyncValidatable<AlbumRules>)edit;

bool valid = await validatable.ValidateAsync(rules, cancellationToken);
```

Use the context form when the validator needs application services or other caller data.

## Add metadata

Metadata can be configured independently from validation rules.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Metadata<Album, string[]>();

IRuntimeTrackingItem<Album> edit =
    options.GetRegistration<IRuntimeTrackingItem<Album>>().Create(original);

((IMetadataWriter<string[]>)edit).SetMetadata(["Title is required"]);

string[] messages = ((IMetadataReader<string[]>)edit).Metadata;
```

Choose reader and writer support separately when only one direction is needed.

```csharp
options.Metadata<Album, string[]>(reader: true, writer: false);
```

Metadata can hold validation messages, UI state, or any other application value. Tracking does not assign a meaning to the metadata type.
