# Supplying values

## Parameter objects

Public readable fields and properties supply values by name.

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

Names match without regard to case.

```csharp
Album album = GetAlbum.Query<Album>(cnn, new { ALBUMID = 1 });
// ALBUMID supplies @albumId.
```

Members with no matching command variable are ignored.

```csharp
Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1, notUsedByThisCommand = true });
// notUsedByThisCommand is ignored.
```

Parameter objects are not limited to anonymous types. Classes, records, and structs with readable public members work too. Custom member rules can support other sources.

```csharp
public sealed class AlbumFilter {
    public int AlbumId { get; init; }
}

public record AlbumFilterRecord(int AlbumId);

public struct AlbumFilterStruct {
    public int AlbumId { get; init; }
}

Album fromClass = GetAlbum.Query<Album>(cnn, new AlbumFilter { AlbumId = 1 });

Album fromRecord = GetAlbum.Query<Album>(cnn, new AlbumFilterRecord(1));

Album fromStruct = GetAlbum.Query<Album, AlbumFilterStruct>(cnn, new AlbumFilterStruct { AlbumId = 1 });
```

Use the `ref` struct overload when copying a large value should be avoided.

```csharp
AlbumFilterStruct filter = new() { AlbumId = 1 };
Album album = GetAlbum.Query<Album, AlbumFilterStruct>(cnn, ref filter);
```

## Null means absent

A `null` member does not supply a database parameter.

```csharp
SearchAlbums.Query<List<Album>>(cnn, new { title = null });
// @title is absent.
```

For a plain required variable, the SQL remains unchanged and execution fails because the parameter is missing.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title
```

For a conditional variable, its SQL is removed.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

See [conditional variables](../conditional-sql/variables.md) for the complete marker rules.

## Send database NULL

Use `DBNull.Value` when a present parameter must contain database `NULL`.

```csharp
ClearTitle.Execute(cnn, new { albumId = 1, title = DBNull.Value });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
-- @title contains database NULL.
```

`[UseDbNull]` keeps a nullable member strongly typed.

```csharp
public sealed class AlbumUpdate {
    public int AlbumId { get; init; }
    [UseDbNull] public string? Title { get; init; }
}

ClearTitle.Execute(cnn, new AlbumUpdate { AlbumId = 1, Title = null });
// @Title is database NULL.
```

Put `[UseDbNull]` on the type when every member should use that rule. A member attribute still wins.

```csharp
[UseDbNull]
public sealed class AlbumUpdate {
    public int AlbumId { get; init; }
    public string? Title { get; init; }
    [NotNullOrWhitespace] public string? Notes { get; init; }
}

UpdateAlbum.Execute(cnn, new AlbumUpdate { AlbumId = 1, Title = null, Notes = null });
// @Title is database NULL. @Notes is absent.
```

## Leave empty text out

`[NotNullOrWhitespace]` treats null, empty, and whitespace strings as absent.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title LIKE ?@Title");

public sealed class AlbumTitleSearch {
    [NotNullOrWhitespace]
    public string? Title { get; init; }
}

SearchAlbums.Query<List<Album>>(cnn, new AlbumTitleSearch { Title = "  " });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

## Leave default values out

`[NotDefault]` treats the member type’s default value as absent.

```csharp
static readonly QueryCommand SearchByYear = new("SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear >= ?@MinimumYear");

public sealed class AlbumYearSearch {
    [NotDefault]
    public int MinimumYear { get; init; }
}

SearchByYear.Query<List<Album>>(cnn, new AlbumYearSearch { MinimumYear = 0 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

A non-default value supplies the parameter.

```csharp
SearchByYear.Query<List<Album>>(cnn, new AlbumYearSearch { MinimumYear = 2000 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear >= @MinimumYear
```

## Supply values with a builder

Use a builder when application logic decides which values and conditions belong to one execution.

```csharp
public sealed class AlbumSearchFilter {
    public int? ArtistId { get; init; }
    [NotNullOrWhitespace] public string? Title { get; init; }
}

static readonly QueryCommand FindAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@ArtistId AND Title LIKE CONCAT('%', ?@Title, '%') AND /*CurrentOnly*/IsArchived = 0");

var search = FindAlbums.StartBuilder();
search.UseWith(filter);
if (restrictedArtistId is int artistId)
    search.Use('@', nameof(AlbumSearchFilter.ArtistId), artistId);
if (!canSeeArchived)
    search.Use("CurrentOnly");
List<Album> albums = search.Query<List<Album>>(cnn);
```

`UseWith` supplies the usable members first. The later `Use` calls can override one of those values or turn on a condition that is not part of the object.

`Use` reports whether the builder was able to use the value.

```csharp
if (!search.Use('@', nameof(AlbumSearchFilter.ArtistId), 12))
    throw new InvalidOperationException("The command has no ArtistId parameter");

bool foundCondition = search.Use("CurrentOnly");  // true
bool wrongKind = search.Use("CurrentOnly", true); // false (used like a parameter)
bool missing = search.Use("@Unknown", 1);          // false
```

Calling `UseWith` again replaces those copied values. A member that is no longer usable clears its earlier value.

```csharp
var search = FindAlbums.StartBuilder();
search.UseWith(new AlbumSearchFilter { ArtistId = 7, Title = "Blue" });
search.UseWith(new AlbumSearchFilter { ArtistId = 12 });
// @Title is now absent.
```

Builder values remain available until removed or reset.

```csharp
search.Use("@ArtistId", 1);
search.Remove("@ArtistId");
search.Reset();
```

The [builder guide](builders.md) shows manual construction, starting from `UseWith`, condition keys, SQL preview, key indexes, and reusable commands.

## Bind a builder to one DbCommand

A bound builder reuses a caller-owned `DbCommand` and updates only what changes.

```csharp
using DbCommand sqlCommand = cnn.CreateCommand();
var batch = InsertAlbum.StartBuilder(sqlCommand);

foreach (Album album in albums) {
    batch.UseWith(album);
    batch.Execute();
}
```

The command already carries its connection and transaction, so bound execution needs neither argument.

## Supply conditional keys from a type

`[ForBoolCond]` turns a boolean member into a conditional key rather than a database parameter.

```csharp
static readonly QueryCommand ReadAlbums = new("SELECT AlbumId AS Id, Title, /*IncludeYear*/ReleaseYear FROM albums WHERE ArtistId = ?@ArtistId");

public sealed class AlbumReadOptions {
    [ForBoolCond] public bool IncludeYear { get; init; }
}

ReadAlbums.Query<List<DynaObject>>(cnn, new AlbumReadOptions { IncludeYear = true });
```

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums
```

`[UsesBoolConds]` activates the same keys whenever that parameter type is used.

```csharp
[UsesBoolConds("IncludeYear")]
public sealed class AlbumReportOptions {
    public int? ArtistId { get; init; }
}
```

```csharp
ReadAlbums.Query<List<DynaObject>>(cnn, new AlbumReportOptions { ArtistId = 1 });
```

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums WHERE ArtistId = @ArtistId
```

The [conditional marker guide](../conditional-sql/variables.md) shows how those keys control conditions, columns, and clauses.

See [parameter member rules](../customization/parameter-members.md) to change how members supply values.

## Positional parameters

Declare variables in provider order when the SQL uses positional placeholders.

```csharp
public record User(int UserId, string Name);

var positional = new QueryCommand("SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?", ["userId", "status"], CommandType.Text);

positional.UpdateParamCache(0, new PositionalDbParamInfo());
positional.UpdateParamCache(1, new PositionalDbParamInfo());

var values = positional.StartBuilder();
values.Use(0, 7);
values.Use(1, "active");

List<User> users = values.Query<List<User>>(cnn);
```

```sql
SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?
-- The provider receives 7, then "active".
```

[Choose a result shape](result-shapes.md).
