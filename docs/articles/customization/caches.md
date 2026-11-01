# Cache control

The examples use an open `DbConnection` named `cnn` and a result type such as `Album`.

## Command lifetime

`QueryCommand` is reusable and can be shared when its lifetime is the application. Dispose a command that owns a shorter-lived cache when that scope ends.

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";

using var command = new QueryCommand(sql);
List<Album> albums = command.Query<List<Album>>(cnn);
```

Disposal invalidates the command's parsers and parameter accessors, releases its mapper, and makes the command unusable. `CachedTypeParser<T>` and the non-generic `CachedTypeParser` are disposable as well, so scope them in the same way when they are not application-lifetime caches.

## QueryCommand parsers

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");

int removed = GetAlbums.InvalidateParsers(static _ => true);
```

The predicate selects the command's cached parsers. The default scope removes command references and removes a parser globally only when no other command still uses it.

```csharp
GetAlbums.InvalidateParsers(static _ => true, QueryParserInvalidationScope.Local);
GetAlbums.InvalidateParsers(static _ => true, QueryParserInvalidationScope.GlobalIfUnused);
GetAlbums.InvalidateParsers(static _ => true, QueryParserInvalidationScope.Global);
GetAlbums.InvalidateParsers(parser => parser.Type == typeof(Album), QueryParserInvalidationScope.GlobalIfUnused);
```

```text
Local           remove this command reference
GlobalIfUnused  also remove the global parser when no other command uses it
Global          remove the parser globally and clear command references to it
```

One exact parser can be selected through the same predicate.

```csharp
GetAlbums.InvalidateParsers(candidate => ReferenceEquals(candidate, parser), QueryParserInvalidationScope.GlobalIfUnused);
```

The predicate can select one type while leaving other result types cached.

```csharp
GetAlbums.InvalidateParsers(parser => parser.Type == typeof(Album));
```

## Parameter accessors

```csharp
public sealed class AlbumFilter
{
    public int ArtistId { get; init; }
}

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @ArtistId");

GetAlbum.Query<List<Album>>(cnn, new AlbumFilter { ArtistId = 7 });

(Type ParameterType, ParameterAccessorKinds Accessors)[] accessors = GetAlbum.GetCachedParameterAccessors();
```

```csharp
GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Direct);
GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.UseWith);
GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Both);
```

These calls affect parameter source accessors. They do not invalidate row parsers.

## SQL string command cache

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";
QueryCommand command = ConnectionQueryExtensions.GetOrCreateCommand(sql);
```

```csharp
bool removed = ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);
```

The exact SQL string is the cache key. Removing the key does not dispose the removed command.

[SQL string access](../running-queries/sql-string.md)

## Generic CachedTypeParser

```csharp
static readonly CachedTypeParser<List<Album>> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";

List<Album> first = AlbumParser.Query(command, disposeCommand: false);
AlbumParser.Invalidate();
List<Album> second = AlbumParser.Query(command, disposeCommand: false);
```

## Non generic CachedTypeParser

```csharp
static readonly CachedTypeParser SchemaParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";

Album album = SchemaParser.Query<Album>(command, disposeCommand: false);
DynaObject row = SchemaParser.Query<DynaObject>(command, disposeCommand: false);

SchemaParser.Invalidate();
// The schema and both result parsers are forgotten together.
```

The non-generic cache treats its learned schema and all parsers as one package. Its next query
learns the schema from its reader again.

[Fixed result schema](../running-queries/fixed-result-schema.md)

## Global parser cache

The examples use a known result schema and a parser obtained from the global cache.

```csharp
ColumnInfo[] columns = TypeSchema<Album>.Schema;
ITypeParser<Album> parser = TypeParser.GetTypeParser<Album>(columns);
```

A predicate can match a schema, a result type, an exact parser instance, or any combination of those conditions.

```csharp
int removed = TypeParser.Invalidate(parser => parser.CanParse(columns), ParserInvalidationMode.CheckUsage);
```

`CheckUsage` leaves an exact parser alive when another cache still reports that it retains it.

```csharp
int removed = TypeParser.Invalidate(candidate => ReferenceEquals(candidate, parser), ParserInvalidationMode.InvalidateReferences);
```

`InvalidateReferences` asks subscribed caches to release that exact parser instance.

The same predicate can select both type and schema.

```csharp
int removed = TypeParser.Invalidate(parser => parser.Type == typeof(Album), ParserInvalidationMode.CheckUsage);
int matching = TypeParser.Invalidate(parser => parser.Type == typeof(Album) && parser.CanParse(columns), ParserInvalidationMode.CheckUsage);
```

```csharp
int removed = TypeParser.Invalidate(static _ => true, ParserInvalidationMode.InvalidateReferences);
```

All globally cached parsers can be removed with the same invalidation mode.

```csharp
bool disposed = TypeParser.Release(parser);
```

`Release` disposes the parser only when neither the global cache nor another subscribed cache still retains it.

[TypeParser API](xref:Rinku.Mapping.TypeParser)
