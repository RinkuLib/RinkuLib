# Quick start

Create a `QueryCommand` from a SQL string, then call an execution method on it.

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

// Your own type. Nothing about it is special to Rinku.
public record Artist(int Id, string Name);

// Create once, in a static readonly field.
static readonly QueryCommand GetArtists = new("SELECT ArtistId AS Id, Name FROM artists");

using DbConnection cnn = GetConnection();

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

No attributes, no configuration. The engine reads the result columns and builds each `Artist` from them.

Two rules carry everything on this page:

1. **Create the command once, reuse it everywhere.** It holds no per-call state and is safe to share across threads.
2. **The type argument decides the result shape.**

```csharp
List<Artist> all       = GetArtists.Query<List<Artist>>(cnn);         // all rows, buffered
IEnumerable<Artist> it = GetArtists.Query<IEnumerable<Artist>>(cnn);  // streamed lazily
Artist one             = GetArtistById.Query<Artist>(cnn, new { id = 1 }); // one row
```

## Parameters

Pass an object whose members match the parameter names.

```csharp
static readonly QueryCommand GetArtistById =
    new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @id");

Artist artist = GetArtistById.Query<Artist>(cnn, new { id = 1 });
```

Anonymous objects, records, and DTOs all work. A member with no matching parameter is ignored.

## Scalars and non-queries

```csharp
static readonly QueryCommand CountArtists = new("SELECT COUNT(*) FROM artists");
static readonly QueryCommand RenameArtist = new("UPDATE artists SET Name = @name WHERE ArtistId = @id");

int total    = CountArtists.ExecuteScalar<int>(cnn);
int affected = RenameArtist.Execute(cnn, new { id = 1, name = "Queen" });
```

## One command that adapts

Mark a variable optional with `?` and the values you pass decide what stays in the SQL.

```csharp
static readonly QueryCommand Search =
    new("SELECT ArtistId AS Id, Name FROM artists WHERE Name LIKE ?@name AND ArtistId > ?@afterId");

List<Artist> results = Search.Query<List<Artist>>(cnn, new { name = "%Black%" });
// SELECT ArtistId AS Id, Name FROM artists WHERE Name LIKE @name
```

`@afterId` was not supplied, so its clause is pruned, along with the dangling `AND`. Markers work the same in any part of any statement, filters, projections, joins, `SET` lists. This is [conditional SQL](../conditional-sql/index.md).

## Where to go next

- [Running queries](../running-queries/index.md). Every way to run a command, by example.
- [Mapping](../mapping/index.md). How rows become objects, from scalars to nested graphs.
- [Conditional SQL](../conditional-sql/index.md). The full template syntax.
