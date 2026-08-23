# Existing DbCommand

## Create a command with context

`DbConnection.GetCommand` creates a provider command and applies the transaction and timeout before the command is configured.

```csharp
using DbTransaction transaction = cnn.BeginTransaction();
using DbCommand command = cnn.GetCommand(transaction, timeout: 30);

command.CommandText = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
transaction.Commit();
```

## Cached parser

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";

DbParameter parameter = command.CreateParameter();
parameter.ParameterName = "@albumId";
parameter.Value = 12;
command.Parameters.Add(parameter);

Album album = AlbumParser.Query(command, disposeCommand: false);
```

`CachedTypeParser<T>` keeps reusable schema and parser information. Reader state remains per execution.

A generated command uses the same parser surface.

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumsParser = new();

List<GetAlbumsByArtistResult> albums = AlbumsParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

[Code generation](../codegen/index.md)

## One schema and several result types

```csharp
public record AlbumSummary(int Id, string Title);

static readonly CachedTypeParser AlbumSchemaParser = new();

using DbCommand first = cnn.CreateCommand();
first.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";
Album album = AlbumSchemaParser.Query<Album>(first);

using DbCommand second = cnn.CreateCommand();
second.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 46";
AlbumSummary summary = AlbumSchemaParser.Query<AlbumSummary>(second);
```

The non generic cache keeps one result schema and can keep parsers for several result types over that schema.

[Fixed result schema](fixed-result-schema.md)

## Execute an existing command

```csharp
using DbCommand command = cnn.CreateCommand();
command.CommandText = "UPDATE albums SET Title = 'Blue' WHERE AlbumId = 12";

int affected = command.Execute(disposeCommand: false);
```

A scalar can use a cached parser.

```csharp
static readonly CachedTypeParser<int> CountParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT COUNT(*) FROM albums";

int count = CountParser.Query(command, disposeCommand: false);
```

Or the command can execute the scalar directly.

```csharp
int count = command.ExecuteScalar<int>(disposeCommand: false);
```

## Command ownership

```csharp
Album album = AlbumParser.Query(command, disposeCommand: false);
// command remains caller owned.
```

```csharp
Album album = AlbumParser.Query(command, disposeCommand: true);
// the parser call disposes command.
```

## Bound builder

```csharp
static readonly QueryCommand InsertAlbum = new("INSERT INTO albums (ArtistId, Title) VALUES (@ArtistId, @Title)");

public readonly record struct AlbumInsert(int ArtistId, string Title);

AlbumInsert[] albums = [new(7, "Blue"), new(7, "Green")];
using DbCommand command = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(command);

foreach (AlbumInsert album in albums)
{
    batch.UseWith(album);
    batch.Execute();
}
```

[Builders](builders.md)
