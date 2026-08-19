# Validation and metadata

Validation and metadata are separate tracking capabilities. A type can implement only what it needs, or use one of the combined contracts.

## Validation

Context-free and contextual validation are separate interfaces.

```csharp
bool valid = item.Validate();
bool validForSave = contextualItem.Validate(saveContext);
```

Asynchronous variants return `ValueTask<bool>` and accept a cancellation token.

```csharp
bool valid = await asyncItem.ValidateAsync(cancellationToken);
bool validForSave = await contextualAsyncItem.ValidateAsync(saveContext, cancellationToken);
```

Collections of validation-capable items can use `ValidateAll` and `ValidateAllAsync`.

## Metadata

`IMetadataReader<TMetadata>` exposes metadata and `IMetadataWriter<TMetadata>` stores it. `IMetadata<TMetadata>` combines both without making metadata a requirement for validation.

```csharp
string[] errors = item.Metadata;
item.SetMetadata(errors);
```

## Validation with metadata

The `IValidation` and `IAsyncValidation` contracts combine validation with metadata reading when that pairing is useful. Async helpers can return both results together.

```csharp
ValidationOutcome<string[]> result = await item.ValidateWithMetadataAsync(cancellationToken);

if (!result.IsValid) {
    string[] errors = result.Metadata;
}
```

Runtime tracking options can attach validation and metadata capabilities to generated edit types. Keep those capabilities separate unless the generated contract intentionally exposes a combined interface.
