# Database NULL

Nullable value types accept database `NULL`.

```csharp
public record Album(int Id, decimal? Price);

Album album = GetAlbum.Query<Album>(cnn);
```

A non nullable value type rejects database `NULL` with `RINKU4003`.

```csharp
public record Album(int Id, decimal Price);
```

## Reference types

Nullable reference annotations are not available as runtime mapping rules. Reference slots accept database `NULL` by default.

Use `[NotNull]` when a reference slot must reject it.

```csharp
public record Album(int Id, [NotNull] string Title);
```

Use `[MaybeNull]` when any slot should accept database `NULL`.

```csharp
public record InventoryItem(int Id, [MaybeNull] int Count);
```

For a value type, accepted database `NULL` becomes the type default value.

## Missing nested objects

Use `[AbortOnNull]` to stop construction when one identity column is database `NULL`.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);
```

A left join row with `LatestAlbumId` set to database `NULL` makes `LatestAlbum` null.

An aborted nested object reaches its containing slot. That slot must accept the missing value or carry another `[AbortOnNull]` rule upward.

```csharp
public record Bottom([AbortOnNull] int Key, string Name) : IDbReadable;
public record Middle(int Id, [AbortOnNull] Bottom Bottom) : IDbReadable;
public record Top(int Id, Middle? Middle);
```

A missing `Bottom` can therefore make `Top.Middle` null.

## Null elements in collections

Null collection elements are skipped by default.

```csharp
public record Palette(int Id, List<string> Colors);
```

Use `[KeepNullElements]` when null elements must be retained.

```csharp
public record Palette(int Id, [KeepNullElements] List<string?> Colors);
```

## Database NULL as the whole result

Row absence and a present database `NULL` are separate result choices.

```csharp
string? title = GetNullableTitle.Query<MaybeNull<string>>(cnn);
OptionalNullable<string> optionalTitle = FindNullableTitle.Query<OptionalNullable<string>>(cnn);
```

See [result shapes](../running-queries/result-shapes.md) for the wrappers that combine null and result count behavior.

## Database NULL in parameters

A null parameter member normally means absent. Use `DBNull.Value` or `[UseDbNull]` when a present database parameter should contain database `NULL`.

See [supplying values](../running-queries/values.md) for database NULL on query parameters.
