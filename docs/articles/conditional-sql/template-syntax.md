# Template syntax

Rinku parses conditional syntax when the `QueryCommand` is created.

```csharp
static readonly QueryCommand SearchAlbums = new("""
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = ?@artistId
""");
```

The parser understands SQL quoting and comments so markers are not taken from ordinary text.

## Variable character

`@` is the default character used for variables.

```sql
WHERE ArtistId = @artistId
```

A command can use another character.

```csharp
static readonly QueryCommand SearchAlbums = new(
    "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = :artistId",
    ':');
```

A builder uses the same character when the value is supplied directly.

```csharp
var builder = SearchAlbums.StartBuilder()
    .Use(':', "artistId", 7);
```

The application default can also be changed.

```csharp
QueryFactory.DefaultVariableChar = ':';
```

## Quoted text

Marker shaped text inside a quoted string is ordinary SQL text.

```sql
SELECT '/*NotAMarker*/' AS Value
```

Bracketed identifiers and backtick identifiers are recognized as quoted identifiers.

```sql
SELECT [/*Column*/], `?@value`
FROM items
```

PostgreSQL dollar quoted strings are recognized too.

```sql
SELECT $$ /*NotAMarker*/ $$
```

## Line comments

Conditional syntax inside a line comment is ignored.

```sql
SELECT AlbumId
FROM albums
-- ?@ignored
WHERE ArtistId = @artistId
```

## Keep a normal block comment

Prefix a block comment with `~` when it must stay a normal SQL comment.

```sql
SELECT AlbumId AS Id
/*~ This comment is sent to the database */
FROM albums
```

## Template validity

Rinku validates its template syntax when the command is created.

The original template does not need to be valid executable SQL before optional pieces are resolved.

```sql
SELECT AlbumId AS Id, Title
FROM albums
WHERE ArtistId = ?@artistId
AND Title = ?@title
```

Each generated form still needs to be valid SQL for the target database.

See [Conditional variables](variables.md) and [Markers](markers.md) for the forms that change the generated SQL.
