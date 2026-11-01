# Fixed result schema

```csharp
public record Album(int Id, string Title);
public record AlbumSummary(int Id, string Title);
public record AlbumRow(int Id, string Title);

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
```

## Learn the schema from the first query

Choose one schema source for a `CachedTypeParser`. The following sections show alternatives; they are not declarations to combine in one class.

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new();

Album album = AlbumSchemaParser.Query<Album>(AlbumCommands.GetAlbum(cnn, 12));
AlbumSummary summary = AlbumSchemaParser.Query<AlbumSummary>(AlbumCommands.GetAlbum(cnn, 12));
```

The first query fixes the returned column schema. Each result type keeps its own parser against that schema.

```csharp
bool learned = AlbumSchemaParser.HasSchema;
ColumnInfo[] columns = AlbumSchemaParser.Schema;
```

## Supply the schema from a type

```csharp
static readonly CachedTypeParser AlbumSchemaParser = CachedTypeParser.From<AlbumRow>();
```

The same schema can be passed directly.

```csharp
static readonly CachedTypeParser AlbumSchemaParser = new(TypeSchema<AlbumRow>.Schema);
```

A runtime type can supply it too.

```csharp
Type rowType = typeof(AlbumRow);
var albumSchemaParser = new CachedTypeParser(rowType);
```

`AlbumRow` describes the columns. It does not force the query result type.

## Supply a reflected construction schema

```csharp
public static class AlbumFactory
{
    public static Album Create(int Id, string Title) => new(Id, Title);
}

static readonly MethodInfo CreateAlbumMethod = typeof(AlbumFactory).GetMethod(nameof(AlbumFactory.Create)) ?? throw new InvalidOperationException();
static readonly CachedTypeParser AlbumSchemaParser = new(SchemaExtractor.FromMethod(CreateAlbumMethod));
```

A constructor can be supplied directly.

```csharp
static readonly ConstructorInfo AlbumRowConstructor = typeof(AlbumRow).GetConstructors()[0];
static readonly CachedTypeParser AlbumSchemaParser = new(AlbumRowConstructor);
```

## Discover a schema from `BasedOn`

`BasedOn` is the runtime schema link. It selects a constructor source type or a named method source,
so it does not require XML documentation files.

```csharp
using Rinku.Mapping;

public static class ResultSchemas
{
    public static void Album(int Id, string Title) { }
}

[BasedOn(typeof(ResultSchemas), null, nameof(ResultSchemas.Album))]
public record Album(int Id, string Title);
```

Enumerate the references and pass the returned member directly to parser creation:

```csharp
foreach ((Type resultType, MemberInfo schemaMember) in typeof(Album).GetSchemas())
    TypeParser.GetTypeParser(resultType, schemaMember);
```

`SchemaReference.GetAllSchemas()` performs the same lookup across every already-loaded assembly.
`SchemaAttribute` is analyzer-only version metadata; runtime schema discovery ignores it.

## Runtime result type

```csharp
Type resultType = typeof(AlbumSummary);
using DbCommand command = AlbumCommands.GetAlbum(cnn, 12);
object? result = AlbumSchemaParser.Query(resultType, command, disposeCommand: false);
```

```csharp
object? result = await AlbumSchemaParser.QueryAsync(resultType, command, disposeCommand: false, ct: cancellationToken);
```

## Async stream

The fixed-schema parser can also stream rows asynchronously from an existing command.

```csharp
using DbCommand command = AlbumCommands.GetAlbum(cnn, 12);

await foreach (Album album in AlbumSchemaParser.StreamQueryAsync<Album>(command, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

## Get a parser without executing

```csharp
ITypeParser<Album> albumParser = AlbumSchemaParser.Get<Album>();
ITypeParser runtimeParser = AlbumSchemaParser.Get(resultType);
```

## Invalidate result parsers

```csharp
AlbumSchemaParser.Invalidate();
```

The non-generic `CachedTypeParser` invalidates its schema and every type parser as one package. The
next query relearns the reader schema, which is useful after an external command-factory change.

[Existing DbCommand](dbcommand.md) · [Cache control](../customization/caches.md)
