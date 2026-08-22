# Build from application logic

Create a builder for one execution flow.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@ArtistId AND Title LIKE ?@Title AND /*CurrentOnly*/IsArchived = 0");

var search = SearchAlbums.StartBuilder();

if (artistId is int id)
    search.Use("@ArtistId", id);

if (!string.IsNullOrWhiteSpace(title))
    search.Use("@Title", title);

if (!canSeeArchived)
    search.Use("CurrentOnly");

List<Album> albums = search.Query<List<Album>>(cnn);
```

A builder holds mutable values and active conditions. It references the reusable `QueryCommand` but does not put per call state on that command.

Create a separate builder for each independent or concurrent execution flow.

## Start from an object

```csharp
public sealed class AlbumSearch
{
    public int? ArtistId { get; init; }

    [NotNullOrWhitespace]
    public string? Title { get; init; }
}

var search = SearchAlbums.StartBuilder();
search.UseWith(new AlbumSearch { ArtistId = 7, Title = "Blue" });
```

`UseWith` supplies every usable member from the source.

## Override one value

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(filter);

if (restrictedArtistId is int artistId)
    search.Use("@ArtistId", artistId);
```

Put manual values after `UseWith` when they should win for that builder state.

## Combine several sources

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new { ArtistId = 7 });
search.UseWith(new { Title = "Blue" });
// Both values are present.
```

A later source changes the values it controls. Unrelated values stay in the builder.

A dictionary changes only keys present in that call.

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new Dictionary<string, object?> { ["ArtistId"] = 7, ["Title"] = "Blue" });
search.UseWith(new Dictionary<string, object?> { ["ArtistId"] = 12 });
// ArtistId is 12.
// Title is still Blue.
```

## Check whether a key exists

```csharp
if (!search.Use("@ArtistId", 12))
    throw new InvalidOperationException("The command has no ArtistId parameter");

bool foundCondition = search.Use("CurrentOnly");
bool missing = search.Use("@Unknown", 1);
```

`Use` returns whether the builder could use the supplied key.

## Materialize default capable parameters

A builder bound to a live command keeps defaults explicit.

```csharp
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", setupConnection);

using DbCommand command = cnn.CreateCommand();
var call = renumberAlbums.StartBuilder(command);

call.UseWith(new { albumId = 12 });
call.SetDefaults();
call.Execute();
```

`SetDefaults()` fills only missing parameters whose parameter metadata can provide a default. Values already supplied to the builder stay in place.

`UseWith` does not call `SetDefaults()` for you. Execution does not call it either.

Calling `SetDefaults()` again does not duplicate defaults that are already materialized.

When a default changes which conditional variables are active, the bound command text is refreshed from the current builder state.

See [parameter metadata](parameter-metadata.md) for reusable parameter metadata and [stored procedures](stored-procedures.md) for discovered output parameters.

## Remove or reset values

```csharp
search.Use("@ArtistId", 1);
search.Remove("@ArtistId");
search.Reset();
```

`Remove` clears one builder key. `Reset` clears the builder state.

## Bind a builder to one DbCommand

```csharp
using DbCommand command = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(command);

foreach (Album album in albums)
{
    batch.UseWith(album);
    batch.Execute();
}
```

A bound builder reuses the caller owned `DbCommand`. Bound execution does not need a connection argument because the command already owns its connection and transaction.

See [existing DbCommand](dbcommand.md) for command ownership. See [conditional SQL](../conditional-sql/variables.md) for the markers controlled by builder values and keys.
