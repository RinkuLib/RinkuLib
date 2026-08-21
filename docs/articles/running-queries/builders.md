# Build from application logic

Use a builder when values and optional SQL are chosen by several branches. The code can start from an object, change individual values, turn conditions on, and then run the completed query.

A builder is per-call state. It holds the mutable values and active conditions for one execution flow while referencing its reusable `QueryCommand`. Independent or concurrent calls should create separate builders; they can safely share the same command. Do not concurrently mutate one builder.

```csharp
public sealed class AlbumSearch {
    public int? ArtistId { get; init; }
    [NotNullOrWhitespace] public string? Title { get; init; }
    [ForBoolCond] public bool IncludeYear { get; init; }
}

static readonly QueryCommand SearchAlbums = new("""
    SELECT AlbumId AS Id, Title, /*IncludeYear*/ReleaseYear
    FROM albums
    WHERE ArtistId = ?@ArtistId
      AND Title LIKE CONCAT('%', ?@Title, '%')
      AND /*CurrentOnly*/IsArchived = 0
    """);
```

## Start from an object and adjust it

`UseWith` fills the builder from an object. Calls to `Use` after it can override a value or add choices that do not belong to that object.

```csharp
static List<DynaObject> FindAlbums(DbConnection cnn, AlbumSearch filter, int? restrictedArtistId, bool canSeeArchived) {
    var search = SearchAlbums.StartBuilder();
    search.UseWith(filter);
    if (restrictedArtistId is int artistId)
        search.Use('@', nameof(AlbumSearch.ArtistId), artistId);
    if (!canSeeArchived)
        search.Use("CurrentOnly");
    return search.Query<List<DynaObject>>(cnn);
}

var filter = new AlbumSearch { ArtistId = 7, Title = "Blue", IncludeYear = true };
List<DynaObject> albums = FindAlbums(cnn, filter, restrictedArtistId: 12, canSeeArchived: false);
```

This call keeps the title filter, overrides the artist with `12`, includes `ReleaseYear`, and keeps archived albums out.

```sql
SELECT AlbumId AS Id, Title, ReleaseYear
FROM albums
WHERE ArtistId = @ArtistId
  AND Title LIKE CONCAT('%', @Title, '%')
  AND IsArchived = 0
```

Put manual changes after `UseWith` when they should override that source. Another `UseWith` call changes only the values controlled by that invocation. Fixed members clear their own unusable values, unrelated sources stay unchanged, and dictionaries affect only keys that are present.

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new { ArtistId = 7 });
search.UseWith(new { Title = "Blue" });
// Both values are present.
```

## Build every choice in code

A parameter object is not required. This version chooses each value and condition directly.

```csharp
var search = SearchAlbums.StartBuilder();
if (artistId is int id)
    search.Use('@', nameof(AlbumSearch.ArtistId), id);
if (!string.IsNullOrWhiteSpace(title))
    search.Use('@', nameof(AlbumSearch.Title), title);
if (includeYear)
    search.Use(nameof(AlbumSearch.IncludeYear));
if (!canSeeArchived)
    search.Use("CurrentOnly");
List<DynaObject> albums = search.Query<List<DynaObject>>(cnn);
```

`Use(name, value)` supplies a parameter. `Use(name)` activates a condition such as `IncludeYear` or `CurrentOnly`.

## Check that a key exists

Both forms of `Use` return `true` when the name exists and has the expected kind. This makes shared query-building code able to reject a command that does not contain a required parameter or condition.

```csharp
static void RequireArtist(QueryBuilder search, int artistId) {
    if (!search.Use('@', nameof(AlbumSearch.ArtistId), artistId))
        throw new InvalidOperationException("This command has no ArtistId parameter");
}
```

The return value is `false` for an unknown name or when the overload does not match the kind of key.

```csharp
var search = SearchAlbums.StartBuilder();
bool foundParameter = search.Use('@', nameof(AlbumSearch.ArtistId), 7); // true
bool foundCondition = search.Use(nameof(AlbumSearch.IncludeYear));     // true
bool wrongKind = search.Use(nameof(AlbumSearch.IncludeYear), true);    // false
bool missing = search.Use("@NotInTheCommand", 1);                      // false
```

Passing the variable character separately keeps the member name inside `nameof` while still addressing `@ArtistId`.

## Change the current state

`Remove` clears either kind of key. `UnUse` clears a condition, and `Reset` clears the complete builder.

```csharp
var search = SearchAlbums.StartBuilder();
search.Use("@ArtistId", 7);
search.Use("IncludeYear");
search.Remove("@ArtistId");
search.UnUse("IncludeYear");
string sql = search.GetQueryText();
// SELECT AlbumId AS Id, Title FROM albums

search.Reset();
```

`GetQueryText` shows the SQL for the current state without running it.

## Start with individual values

Values known when the builder is created can be passed to `StartBuilder`.

```csharp
var search = SearchAlbums.StartBuilder([("@ArtistId", 7), ("@Title", "Blue")]);
if (includeYear)
    search.Use("IncludeYear");
List<DynaObject> albums = search.Query<List<DynaObject>>(cnn);
```

## Reuse one DbCommand

Pass a `DbCommand` to `StartBuilder` for a batch. The builder updates that same command for every item, and execution no longer needs a connection argument.

```csharp
public record AlbumDraft(string Title, int ReleaseYear);

static readonly QueryCommand InsertAlbum = new("INSERT INTO albums (Title, ReleaseYear) VALUES (@Title, @ReleaseYear)");

using DbCommand sqlCommand = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(sqlCommand);
AlbumDraft[] drafts = [new("Blue", 2024), new("Green", 2025)];
foreach (AlbumDraft album in drafts) {
    batch.UseWith(album);
    batch.Execute();
}
```

Each `AlbumDraft` has the same shape, so its `UseWith` call replaces the values from the previous item while the builder keeps the same `DbCommand`. Values outside that source are not reset.

## Use key indexes

Code that already knows a key index can avoid looking it up again.

```csharp
int artistIdIndex = SearchAlbums.Mapper.GetIndex("@ArtistId");
int includeYearIndex = SearchAlbums.Mapper.GetIndex("IncludeYear");

var search = SearchAlbums.StartBuilder();
search.Use(artistIdIndex, 7);
search.Use(includeYearIndex);

List<DynaObject> albums = search.Query<List<DynaObject>>(cnn);
```

Indexes also supply values for [positional parameters](values.md#positional-parameters).

[See every way to supply values](values.md).
