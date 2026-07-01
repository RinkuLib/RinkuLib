# Quick start

The smallest useful task is to run a query and get objects back. Create a `QueryCommand` from a plain SQL string, then call an execution method straight on it.

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

// Your own type. The engine has no built-in knowledge of it.
public record Artist(int Id, string Name);

// Create once, parsing and caches happen here. A static readonly field is ideal.
static readonly QueryCommand GetArtists = new("SELECT ArtistId AS Id, Name FROM artists");

using DbConnection cnn = GetConnection();

List<Artist> artists = GetArtists.Query<List<Artist>>(cnn);
```

No attributes and no configuration are needed. The engine reads the result columns and builds a parser that produces `Artist` from each row.

The **type argument decides the shape** of the result.

```csharp
List<Artist> all      = GetArtists.Query<List<Artist>>(cnn);        // a buffered list
IEnumerable<Artist> it = GetArtists.Query<IEnumerable<Artist>>(cnn); // streamed lazily
Artist one            = GetArtistById.Query<Artist>(cnn, new { id = 1 }); // a single object
```

`Artist` here is just an example. The same holds for any class, record, or struct you ask for.

## Parameters

Pass an object whose members match the parameter names.

```csharp
static readonly QueryCommand GetArtistById =
    new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @id");

Artist artist = GetArtistById.Query<Artist>(cnn, new { id = 1 });
```

Anonymous objects, records, and DTOs all work. A member with no matching parameter is simply ignored.

## Scalars and non-queries

`ExecuteScalar<T>` and `Execute` cover statements that return a single value or no rows.

```csharp
static readonly QueryCommand CountArtists = new("SELECT COUNT(*) FROM artists");
static readonly QueryCommand RenameArtist = new("UPDATE artists SET Name = @name WHERE ArtistId = @id");

int total    = CountArtists.ExecuteScalar<int>(cnn);
int affected = RenameArtist.Execute(cnn, new { id = 1, name = "Queen" });
```

## The same call adapts to its input

Mark the optional parts of a template, and the values you pass decide what stays in the SQL.

```csharp
static readonly QueryCommand Search =
    new("SELECT ArtistId AS Id, Name FROM artists WHERE Name LIKE ?@name AND ArtistId > ?@afterId");

// @afterId omitted, so its clause is pruned.
// SELECT ArtistId AS Id, Name FROM artists WHERE Name LIKE @name
List<Artist> results = Search.Query<List<Artist>>(cnn, new { name = "%Black%" });
```

The markers live in the SQL template; the call itself is unchanged. Full details are in [conditional SQL](../conditional-sql/overview.md).

## What happened

- The `QueryCommand` parsed your SQL and set up its caches, once. Parsing reads the optional markers; a variable without one is never pruned, so this unmarked query runs as written. Markers are [conditional SQL](../conditional-sql/overview.md).
- `Query<T>` ran the command, built a mapping from the result columns to your type, and cached it by result shape. So the next call skips that work.

## Where to go next

- [Core concepts](core-concepts.md). The mental model behind these calls.
- [Running queries](../executing/overview.md). Every shape of call, by example.
- [Mapping](../mapping/index.md). How rows become objects, from scalars to nested graphs.
