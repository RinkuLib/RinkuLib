# Validation and metadata

The examples use the `Album`, `IAlbumEdit`, `original`, and `cnn` setup from the [tracking overview](index.md).

## Validation

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
options.Validate<Album, IAlbumEdit>(static edit => !string.IsNullOrWhiteSpace(edit.Title));

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
bool valid = ((IValidatable)edit).Validate();
```

These optional generated methods are invoked explicitly by their caller. Tracking does not discover or run validators automatically. Applications can instead record validation in an ordinary executor plan and pass the compiled validation delegate to their committer.

## Validation with caller context

```csharp
options.Validate<Album, IAlbumEdit, HashSet<string>>(static (edit, reservedTitles) => !reservedTitles.Contains(edit.Title));

IValidatable<HashSet<string>> validatable = (IValidatable<HashSet<string>>)edit;
bool valid = validatable.Validate(reservedTitles);
```

The context type is supplied by application code at validation time.

## Operation result validation

Operation result validation is distinct from validation performed before persistence.

```csharp
options.ValidateResult<Album, IAlbumEdit, AlbumSaveResult>(static (edit, result) => result.Accepted && result.AlbumId > 0);

IResultValidatable<AlbumSaveResult> validatable = (IResultValidatable<AlbumSaveResult>)edit;
AlbumSaveResult result = new(true, 12);
bool valid = validatable.ValidateResult(result);
```

The committer owns interpretation of its results. Its configured result callback can reject the transaction and publish application metadata. The tracking core does not interpret result types or copy results into metadata.

Output-producing committers buffer outputs until all results are accepted and persistence commits. A typed construction delegate can use those outputs and edited values to create an accepted original before the transaction commits; its output is installed afterward, before tracking confirmation.

## Async validation

```csharp
options.ValidateAsync<Album, IAlbumEdit>(static (edit, cancellationToken) => ValueTask.FromResult(!string.IsNullOrWhiteSpace(edit.Title)));

IAsyncValidatable validatable = (IAsyncValidatable)edit;
bool valid = await validatable.ValidateAsync(cancellationToken);
```

## Async validation with context

```csharp
options.ValidateAsync<Album, IAlbumEdit, HashSet<string>>(static (edit, reservedTitles, cancellationToken) => ValueTask.FromResult(!reservedTitles.Contains(edit.Title)));

IAsyncValidatable<HashSet<string>> validatable = (IAsyncValidatable<HashSet<string>>)edit;
bool valid = await validatable.ValidateAsync(reservedTitles, cancellationToken);
```

The context object is supplied by the caller.

## Validation result storage

Validation results can be stored as ordinary additional members. An interface gives consumers typed access without a special tracking storage feature:

```csharp
public interface IValidationState {
    string[]? Validation { get; set; }
}

var options = RuntimeTracking.CreateOptions<Album>()
    .Member((IValidationState state) => state.Validation, member => member.Direct().Parameters(false));

var edit = options.GetRegistration<ITrackingItem<Album>>().Create(original);
((IValidationState)edit).Validation = ["Title is required"];
```

A direct member remains independent of the accepted model and the edit snapshot. Its lifetime and reset policies are configured separately.

`Rinku.More` provides `WithValidation()` to configure its standard `IValidationState.Validation` member through the same interface-selector API. The convenience does not introduce another storage or registration mechanism.
