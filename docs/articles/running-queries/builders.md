# Builders

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title AND /*CurrentOnly*/IsArchived = 0");

var search = SearchAlbums.StartBuilder();
search.Use("@artistId", 7);
search.Use("CurrentOnly");

List<Album> albums = search.Query<List<Album>>(cnn);
```

The builder holds the values and active conditions for one execution flow. The `QueryCommand` stays reusable.

## Add values

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new { artistId = 7 });
search.UseWith(new { title = "Blue%" });

List<Album> albums = search.Query<List<Album>>(cnn);
```

A later source changes only the values it supplies.

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new Dictionary<string, object?> { ["artistId"] = 7, ["title"] = "Blue%" });
search.UseWith(new Dictionary<string, object?> { ["artistId"] = 12 });

Console.WriteLine(search["@artistId"]); // 12
Console.WriteLine(search["@title"]);    // Blue%
```

A struct can be supplied by reference.

```csharp
public readonly record struct AlbumSearch(int ArtistId, string Title);

var search = SearchAlbums.StartBuilder();
AlbumSearch filter = new(7, "Blue%");
search.UseWith(ref filter);
```

## Seed a builder

```csharp
var search = SearchAlbums.StartBuilder(("@artistId", 7), ("@title", "Blue%"));
```

The seeded values use the same builder state as later `Use` and `UseWith` calls.

## Conditions

```csharp
bool enabled = search.Use("CurrentOnly");
bool disabled = search.UnUse("CurrentOnly");
```

`Use` and `UnUse` return `false` when the supplied name is not a condition owned by the command.

```csharp
bool foundParameter = search.Use("@artistId", 12);
bool missingParameter = search.Use("@unknown", 12);
```

The value overload returns `false` when the supplied name is not a value slot owned by the command.

[Conditional markers](../conditional-sql/markers.md)

## Inspect the current state

```csharp
object? artistId = search["@artistId"];
string sql = search.GetQueryText();
```

`GetQueryText()` parses the template from the current builder state without executing it.

## Remove and reset

```csharp
search.Remove("@title");
search.UnUse("CurrentOnly");
search.Reset();
```

`Remove` clears one key. `Reset` clears the complete builder state.

## Bind to a DbCommand

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

The bound builder keeps the same caller owned `DbCommand` while values change.

A bound builder can also be seeded.

```csharp
using DbCommand command = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(command, ("@ArtistId", 7), ("@Title", "Blue"));
batch.Execute();
```

## Materialize known defaults

```csharp
QueryCommand updateAlbum = QueryCommand.FromProc("UpdateAlbum", cnn);

using DbCommand command = cnn.CreateCommand();
var call = updateAlbum.StartBuilder(command);

call.UseWith(new { albumId = 12 });
call.SetDefaults();
call.Execute();
```

`SetDefaults()` exists on the builder bound to a live command. It fills missing parameters whose metadata can provide a default. Existing builder values stay in place.

[Stored procedures](stored-procedures.md) · [Parameter metadata](parameter-metadata.md) · [Existing DbCommand](dbcommand.md)
