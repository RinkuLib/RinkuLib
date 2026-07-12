# RinkuLib: A Modular Micro-ORM

A micro-ORM for .NET, built directly on **ADO.NET**. You write the SQL, name a type, and get your objects back.

```csharp
public record Album(int Id, string Title);

// Create the command once (a static readonly field is ideal). Parsing happens here.
static readonly QueryCommand GetAlbums =
    new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 1 });
// GetAlbums.Query<Album>(cnn, ...)               -> a single album
// GetAlbums.Query<IEnumerable<Album>>(cnn, ...)  -> streamed
```

The result shape follows the type argument. `Album` is one of your own types, any class, record, or struct works, with no attributes and no configuration. Reach for the capabilities one at a time or together.

- **Object mapping.** Compile a mapping from the result schema to your type through a configurable negotiation.
- **Conditional SQL.** One template that adapts to the values you pass, valid without string concatenation.
- **Code generation.** Generate ready-to-run `DbCommand`s from your database schema at design time.
- **Tracking.** Edit, commit, and revert change tracking over an `IEnumerable`.

Mapping is the spine, and the rest builds on it. Targets .NET 8 and .NET 10.

Full documentation: <https://rinkulib.github.io/RinkuLib/>.

## Conditional SQL

When a query must change shape at runtime, mark the optional parts (`?@var`, `/*...*/`) and the values you supply decide what stays.

```csharp
static readonly QueryCommand Search =
    new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE ?@title");

// @title omitted, so its clause is pruned.
List<Album> albums = Search.Query<List<Album>>(cnn, new { artistId = 1 });
// Resulting SQL: SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

## How it works

You define the template first, so your code only decides what's used and never concatenates SQL, the statement stays valid wherever a value lands, with no `WHERE 1=1`. Mapping works the same way. A configurable negotiation maps the flat result onto the shape of your C# type, and the type decides how the columns nest and how many rows it takes.

## Links

- Documentation: <https://rinkulib.github.io/RinkuLib/>
- Source: <https://github.com/RinkuLib/RinkuLib>
