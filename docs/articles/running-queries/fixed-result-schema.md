# Fixed result schema

Use the non generic `CachedTypeParser` when one returned column schema can be read as several result types.

It is normally kept beside the command factory whose results it reads.

```csharp
public record AlbumSummary(int Id, string Title);

static readonly CachedTypeParser GetAlbumParser = new();

public static class AlbumCommands
{
    public static DbCommand GetAlbum(DbConnection cnn, int albumId)
    {
        DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@albumId";
        parameter.Value = albumId;
        command.Parameters.Add(parameter);

        return command;
    }
}

Album album = GetAlbumParser.Query<Album>(AlbumCommands.GetAlbum(cnn, 12));
AlbumSummary summary = GetAlbumParser.Query<AlbumSummary>(AlbumCommands.GetAlbum(cnn, 12));
```

The first query learns the returned columns. Later result types reuse that schema and keep their own parser in the same cache.

The exact command or factory is not enforced. Another command can use the same cache when it returns compatible columns.

## Learn the schema from the first query

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new();

Album album = AlbumSchemaParser.Query<Album>(AlbumCommands.GetAlbum(cnn, 12));
DynaObject row = AlbumSchemaParser.Query<DynaObject>(AlbumCommands.GetAlbum(cnn, 12));
```

Check whether the schema is already known when that matters to the caller.

```csharp
bool learned = AlbumSchemaParser.HasSchema;
```

Read the fixed columns after they are known.

```csharp
ColumnInfo[] columns = AlbumSchemaParser.Schema;
```

## Supply the schema before the first query

Use `From<TSchema>()` when a type already describes the returned columns.

```csharp
public record AlbumRow(int Id, string Title);

static readonly CachedTypeParser AlbumSchemaParser = CachedTypeParser.From<AlbumRow>();

Album album = AlbumSchemaParser.Query<Album>(AlbumCommands.GetAlbum(cnn, 12));
AlbumSummary summary = AlbumSchemaParser.Query<AlbumSummary>(AlbumCommands.GetAlbum(cnn, 12));
```

`AlbumRow` describes the columns. It does not force the query result to be `AlbumRow`.

The same schema can be supplied directly.

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new(TypeSchema<AlbumRow>.Schema);
```

A runtime `Type` can supply the schema too.

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new(typeof(AlbumRow));
```

## Describe columns from reflection

`SchemaExtractor` can describe method or constructor parameters when those parameters already represent the required columns.

```csharp
public static class AlbumFactory
{
    public static Album Create(int Id, string Title) => new(Id, Title);
}

static readonly MethodInfo CreateAlbumMethod = typeof(AlbumFactory).GetMethod(nameof(AlbumFactory.Create)) ?? throw new InvalidOperationException();

static readonly CachedTypeParser AlbumSchemaParser = new(SchemaExtractor.FromMethod(CreateAlbumMethod));
```

A constructor can be used directly.

```csharp
static readonly ConstructorInfo AlbumRowConstructor = typeof(AlbumRow).GetConstructors()[0];

static readonly CachedTypeParser AlbumSchemaParser = new(AlbumRowConstructor);
```

## Runtime result types

Use the non generic overload when the result type is known only at runtime.

```csharp
Type resultType = typeof(AlbumSummary);
using DbCommand command = AlbumCommands.GetAlbum(cnn, 12);
object? result = AlbumSchemaParser.Query(resultType, command, disposeCommand: false);
```

The asynchronous form follows the same rule.

```csharp
object? result = await AlbumSchemaParser.QueryAsync(resultType, command, disposeCommand: false, ct: cancellationToken);
```

## Get a parser without running a command

Once the schema is known, get the parser for a result type directly.

```csharp
ITypeParser<Album> albumParser = AlbumSchemaParser.Get<Album>();
ITypeParser runtimeParser = AlbumSchemaParser.Get(resultType);
```

## Invalidate cached result parsers

Remove one result parser while keeping the fixed schema and the other result parsers.

```csharp
AlbumSchemaParser.Invalidate<Album>();
```

Remove every result parser held by this cache.

```csharp
AlbumSchemaParser.Invalidate();
```

The schema remains fixed for the lifetime of the `CachedTypeParser` instance.

Use [existing DbCommand](dbcommand.md) for the generic cache and normal command factory usage. Use [cache control](../customization/caches.md) for parser invalidation.
