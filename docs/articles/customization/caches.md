# Cache ownership

Cache controls remove references to parsers, parameter accessors, or SQL-string commands. They do not change the mapping and binding systems those entries use.

## Parsers kept by a QueryCommand

Remove every parser kept by one command.

```csharp
int removed = GetAlbums.InvalidateParsers();
```

Keep the global parser while dropping only this command's references.

```csharp
GetAlbums.InvalidateParsers(QueryParserInvalidationScope.Local);
```

Invalidate one parser selected for a builder's current key combination.

```csharp
var values = GetAlbum.StartBuilder();
values.Use("@albumId", 12);

Album album = values.Query<Album>(cnn);

if (GetAlbum.TryGetCachedParser<Album>(values.Variables, out ITypeParser<Album>? parser)) {
    GetAlbum.InvalidateParser(parser, QueryParserInvalidationScope.Global);
}
```

## Parameter-object accessors

Direct parameter objects and builder `UseWith` retain separate accessors.

```csharp
foreach (var cached in GetAlbum.GetCachedParameterAccessors())
    Console.WriteLine($"{cached.ParameterType} {cached.Accessors}");
```

```csharp
GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Direct);

GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.UseWith);

GetAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Both);
```

Accessor invalidation does not remove row parsers.

## QueryCommands kept by SQL strings

SQL-string extensions retain their commands in `ConnectionQueryExtensions.CommandCache`.

```csharp
QueryCommand command =
    ConnectionQueryExtensions.GetOrCreateCommand(sql);

bool removed =
    ConnectionQueryExtensions.CommandCache.TryRemove(sql, out QueryCommand? cached);
```

Removing the dictionary entry does not dispose the command.

See [SQL-string shortcuts](../running-queries/sql-string.md) for the global cache and manual keys.

## One fixed type over a learned schema

`CachedTypeParser<T>` learns its schema from the first command.

```csharp
using var cache = new CachedTypeParser<List<Album>>();

List<Album> first = cache.Query(albumsCommand);

bool removed = cache.Invalidate();

List<Album> second = cache.Query(albumsCommand);
```

Disposing the cache releases its saved parser.

## Several types over a known schema

A non-generic `CachedTypeParser` can retain a parser for each requested result type.

```csharp
ColumnInfo[] columns = TypeSchema<Album>.Schema;
using var cache = new CachedTypeParser(columns);
using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums";

List<Album> albums = cache.Query<List<Album>>(command);
DynaObject first = cache.Query<DynaObject>(command);

cache.Invalidate<List<Album>>();
// The DynaObject parser remains cached.
```

## Several types over a learned schema

The parameterless cache learns one schema from its first command.

```csharp
using var cache = new CachedTypeParser();

List<Album> albums = cache.Query<List<Album>>(albumsCommand);
DynaObject first = cache.Query<DynaObject>(albumsCommand);

cache.Invalidate();
// Parsers are removed. The learned schema remains.
```

Every later command used through that cache must return the same schema.

## Global parser invalidation

Invalidate every global parser compatible with one schema.

```csharp
TypeParser.Invalidate(columns, ParserInvalidationMode.CheckUsage);
```

Invalidate one exact parser and tell subscribed caches to release it.

```csharp
TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
```

`CheckUsage` allows another owner to keep the parser alive. `InvalidateReferences` asks every subscribed cache to drop that exact instance before disposal.

## Listen from an application cache

An application cache can participate in global parser ownership.

```csharp
sealed class AppParserCache(ITypeParser parser) : IDisposable {
    ITypeParser? retainedParser = parser;

    public void Start() => TypeParser.ParserDisposing += OnParserDisposing;

    void OnParserDisposing(object? sender, ParserDisposingEventArgs args) {
        if (!ReferenceEquals(args.Parser, retainedParser))
            return;

        if (args.Mode == ParserInvalidationMode.CheckUsage)
            args.Cancel = true;
        else
            retainedParser = null;
    }

    public void Dispose() {
        TypeParser.ParserDisposing -= OnParserDisposing;
        TypeParser.Release(retainedParser);
        retainedParser = null;
    }
}
```

`Release` disposes the parser only after every owner has released the same instance.
