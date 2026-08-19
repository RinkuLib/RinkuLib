# Parameter metadata

The first execution lets the provider infer ordinary parameter metadata.

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

RenameAlbum.Execute(cnn, new { albumId = 12, title = "Blue" });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

After execution, the default inference path stores `DbType`. Sized text and binary parameters also store a widened size.

```text
Provider result for @title: DbType.String, Size 4
Cached for @title:          DbType.String, Size 100
```

Later executions apply the cached metadata before assigning the new value.

```csharp
RenameAlbum.Execute(cnn, new { albumId = 12, title = "A much longer title" });
```

```text
Applied before execution: DbType.String, Size 100
Assigned value:            A much longer title
```

The cached metadata is not inferred again. A later incompatible value may be converted, truncated, or rejected by the provider.

## Size buckets

The default buckets use the size reported by the provider after execution.

```text
Reported Size -1         -> cached Size -1
Other Size <= 100        -> cached Size 100
Reported Size 101..500   -> cached Size 500
Reported Size 501..4000  -> cached Size 4000
Reported Size > 4000     -> cached Size -1
```

Zero enters the first bucket. A provider-reported unbounded size remains unbounded.

```text
Reported Size 0   -> cached Size 100
Reported Size -1  -> cached Size -1
```

Size is retained only for these database types.

```csharp
DbType[] sizedTypes = [
    DbType.String,
    DbType.AnsiString,
    DbType.Binary,
    DbType.Xml,
    DbType.AnsiStringFixedLength,
    DbType.StringFixedLength
];
```

Every other database type retains only `DbType` through ordinary post-execution inference.

```text
Provider result: DbType.Decimal, Precision 18, Scale 4
Default cache:   DbType.Decimal
```

Direction, precision, and scale are not learned by this path.

## Pin one parameter

Replace one ordinary parameter strategy when its database metadata is known beforehand.

```csharp
RenameAlbum.UpdateParamCache("@title", TypedDbParamCache.Get(DbType.String, 500));
```

```text
Every execution: DbType.String, Size 500
```

`UpdateParamCache` replaces an inferred, learned, or previously pinned ordinary strategy. A parameter owned by a special handler is reset through that handler instead.

## Learn again

Reset the command before the next execution when a cached type or size should be inferred again.

```csharp
RenameAlbum.Parameters.Reset();

RenameAlbum.Execute(cnn, new { albumId = 12, title = "A title whose metadata should be learned again" });
```

Every bindable ordinary strategy returns to `DbParameterDefaults.Current.Inferred`. Every special handler receives `ResetCache(inferred)`.

The built-in multi-value handler resets its element strategy.

```csharp
static readonly QueryCommand FindAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_X)");

FindAlbums.Parameters.Reset();
// The cached metadata for each expanded element will be learned again.
```

A custom handler that owns more cached strategies overrides `ResetCache`. The base implementation resets only its `IsCached` state.

## Metadata discovered from a procedure

`FromProc` copies declared metadata instead of using ordinary post-execution inference.

```csharp
QueryCommand RenumberAlbums = QueryCommand.FromProc("RenumberAlbums", setupConnection);
```

```text
Copied exactly when declared: DbType, precision, scale
Otherwise copied exactly:     DbType, size
Also copied:                  output direction and return-value metadata
```

The copied strategy remains in use until it is explicitly replaced or reset.

The stored-procedure return value is declared command metadata, not a bindable `QueryParameters` entry.

```csharp
RenumberAlbums.Parameters.Reset();

RenumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command) {
    int returnValue = command.GetReturnValue<int>();
}
```

`Parameters.Reset()` resets `albumId`, `moved`, and handler-owned strategies. It does not reset the return-value strategy. The return parameter has no caller value or placeholder and is recreated automatically with its declared type, size, precision, scale, and `ReturnValue` direction.

There is currently no public operation that resets or replaces the return-value strategy.

## Provider-specific discovery

Some providers expose size, precision, scale, or other parameter metadata that the default learning path does not keep. Register an `IDbParamInfoGetter` when a command needs that metadata copied into its parameter plan.

The full getter and its registration are shown in [parameter binding customization](../customization/parameters.md).
