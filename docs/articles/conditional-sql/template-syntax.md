# Template syntax

## Parsed when the command is created

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId");
```

Conditional syntax is parsed into the command template at construction time.

## Variable character

```sql
WHERE ArtistId = @artistId
```

A command can use another variable character.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = :artistId", ':');

var builder = SearchAlbums.StartBuilder();
builder.Use(':', "artistId", 7);
```

The application default can also be changed.

```csharp
QueryFactory.DefaultVariableChar = ':';
```

## Quoted text

```sql
SELECT '/*NotAMarker*/' AS Value
```

```sql
SELECT [/*Column*/], `?@value` FROM items
```

```sql
SELECT $$ /*NotAMarker*/ $$
```

Marker shaped text inside recognized quoted SQL remains ordinary text.

## Line comment

```sql
SELECT AlbumId FROM albums
-- ?@ignored
WHERE ArtistId = @artistId
```

Conditional syntax inside a line comment is ignored.

## Keep a block comment

```sql
SELECT AlbumId AS Id /*~ sent to the database */ FROM albums
```

`/*~` marks a block comment that stays in generated SQL.

## Template and generated SQL

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title = ?@title
```

The template can contain conditional syntax that is not executable SQL before resolution. Each generated form still needs to be valid SQL for its target database.

[Conditional variables](variables.md) · [Markers](markers.md)
