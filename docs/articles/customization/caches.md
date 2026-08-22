# Cache control

Cache invalidation is normally needed only after application configuration changes while the process is already running.

## Parsers kept by a command

Remove parsers referenced by one `QueryCommand`.

```csharp
int removed = GetAlbums.InvalidateParsers();
```

Keep the global parser and remove only this command reference.

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Local);
```

Use global invalidation when the parser itself should no longer be reused.

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Global);
```

## Parameter source accessors

Parameter objects and builder `UseWith` use cached accessors.

```csharp
GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Direct);

GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.UseWith);
```

Use `ParameterAccessorKinds.Both` to invalidate both forms.

This does not invalidate row parsers.

## SQL string commands

SQL string shortcuts keep their `QueryCommand` instances in a global cache.

```csharp
QueryCommand command = ConnectionQueryExtensions.GetOrCreateCommand(sql);
```

Remove one exact SQL key when needed.

```csharp
bool removed = ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);
```

Removing the cache entry does not dispose the command.

See [SQL string shortcuts](../running-queries/sql-string.md) for normal use.

## CachedTypeParser

A generic `CachedTypeParser<T>` is normally a long lived cache kept beside the command factory it serves.

```csharp
static readonly CachedTypeParser<List<Album>> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";

List<Album> first = AlbumParser.Query(command, disposeCommand: false);

AlbumParser.Invalidate();

List<Album> second = AlbumParser.Query(command, disposeCommand: false);
```

The generic cache keeps one result type. A different command can use the same cache when its returned schema is compatible.

A non generic cache keeps one fixed schema and can cache several result parsers over it.

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";

Album album = AlbumSchemaParser.Query<Album>(command, disposeCommand: false);
DynaObject row = AlbumSchemaParser.Query<DynaObject>(command, disposeCommand: false);

AlbumSchemaParser.Invalidate<Album>();
// The DynaObject parser remains cached.
```

The first query learns the schema. Use [fixed result schema](../running-queries/fixed-result-schema.md) when the schema should be supplied before the first query or when the result type is known only at runtime.

## Global parser invalidation

Use the global API when configuration changes affect parsers beyond one command.

```csharp
TypeParser.Invalidate(columns, ParserInvalidationMode.CheckUsage);
```

Use `InvalidateReferences` when owners should release one exact parser instance.

```csharp
TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
```

Configuring mappings and parser makers during startup avoids runtime invalidation for normal application use.
