# Existing DbCommand

Use `CachedTypeParser<T>` when a `DbCommand` already exists and the same result schema will be parsed repeatedly.

The cache is normally kept beside the command factory.

```csharp
public record Album(int Id, string Title);
public record AlbumSummary(int Id, string Title);

static readonly CachedTypeParser<Album> GetAlbumParser = new();

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

Album album = GetAlbumParser.Query(AlbumCommands.GetAlbum(cnn, 12));
```

The cache keeps reusable parser and schema information. It does not keep per call parser state.

The command can come from application code, generated code, a stored procedure wrapper, or another component. Another command can use the same cache when it returns a compatible schema for `Album`.

For example, the same parser can consume a generated command.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumsParser = new();

List<GetAlbumsByArtistResult> albums = AlbumsParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

See [code generation](../codegen/index.md) for generated `DbCommand` methods.

When one returned schema can be read as several result types, use the non generic `CachedTypeParser`.

```csharp
static readonly CachedTypeParser GetAlbumSchemaParser = new();

Album album = GetAlbumSchemaParser.Query<Album>(AlbumCommands.GetAlbum(cnn, 12));
AlbumSummary summary = GetAlbumSchemaParser.Query<AlbumSummary>(AlbumCommands.GetAlbum(cnn, 12));
```

The first query learns the schema. See [fixed result schema](fixed-result-schema.md) for explicit schemas and runtime result types.

A command factory that returns several rows naturally gets its own matching cache.

```csharp
static readonly CachedTypeParser<List<Album>> GetAlbumsParser = new();

public static class AlbumListCommands
{
    public static DbCommand GetAlbums(DbConnection cnn, int artistId)
    {
        DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId ORDER BY AlbumId";

        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@artistId";
        parameter.Value = artistId;
        command.Parameters.Add(parameter);

        return command;
    }
}

List<Album> albums = GetAlbumsParser.Query(AlbumListCommands.GetAlbums(cnn, 12));
```

## Execute an existing command

```csharp
using DbCommand updateCommand = cnn.CreateCommand();
updateCommand.CommandText = "UPDATE albums SET Title = 'Blue' WHERE AlbumId = 1";

int affected = updateCommand.Execute(disposeCommand: false);
```

A scalar value can use a long lived parser cache.

```csharp
static readonly CachedTypeParser<int> CountAlbumsParser = new();

using DbCommand countCommand = cnn.CreateCommand();
countCommand.CommandText = "SELECT COUNT(*) FROM albums";

int count = CountAlbumsParser.Query(countCommand, disposeCommand: false);
```

Or it can use `ExecuteScalar<T>`.

```csharp
int count = countCommand.ExecuteScalar<int>(disposeCommand: false);
```

## Command ownership

The command remains caller owned when `disposeCommand` is false.

```csharp
Album album = GetAlbumParser.Query(command, disposeCommand: false);
// command is still available.
```

Pass true when the execution call should dispose it.

```csharp
Album album = GetAlbumParser.Query(command, disposeCommand: true);
```

## Reuse one command with a builder

```csharp
using DbCommand command = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(command);

foreach (Album album in albums)
{
    batch.UseWith(album);
    batch.Execute();
}
```

See [builders](builders.md) for mutable per execution values.
