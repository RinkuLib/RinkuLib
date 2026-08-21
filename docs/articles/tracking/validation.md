# Validation and metadata

Validation and metadata are optional generated capabilities. `TrackingList<T>` does not know they exist.

## Validation

Context-free and contextual validation use separate interfaces.

```csharp
using Rinku.Tracking;

static bool IsValid(IValidatable item) => item.Validate();
static bool IsValidForSave<TContext>(IValidatable<TContext> item, TContext context) => item.Validate(context);
```

Asynchronous variants return `ValueTask<bool>` and accept a cancellation token.

```csharp
static ValueTask<bool> IsValidAsync(IAsyncValidatable item, CancellationToken cancellationToken) =>
    item.ValidateAsync(cancellationToken);
```

Runtime options attach handlers and the required interface to the same generated type.

```csharp
using Rinku.Tracking.Runtime;

public sealed class Album { public string? Title { get; set; } }
public interface IAlbumEdit { string? Title { get; set; } }

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(new Album { Title = "Blue" });
bool valid = ((IValidatable)edit).Validate();
```

## Metadata

`IMetadataReader<TMetadata>` exposes metadata, `IMetadataWriter<TMetadata>` stores it, and `IMetadata<TMetadata>` combines both. Metadata is direct generated-object state and remains outside the edit/original lifecycle.

```csharp
static void ReplaceErrors(IMetadata<string[]> metadata, string[] errors)
{
    metadata.SetMetadata(errors);
    string[] current = metadata.Metadata;
}
```

Use `options.Metadata<TOriginal, TMetadata>()` to add metadata capabilities to a generated type. Validation and metadata stay independent unless the selected consumer interface deliberately combines them.
