# Supplying values

Readable public fields and properties supply values by name.

```csharp
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

Names match without regard to case. Members that do not match a command variable are ignored.

```csharp
Album album = GetAlbum.Query<Album>(cnn, new { ALBUMID = 1, unused = true });
// ALBUMID supplies @albumId.
// unused is ignored.
```

Classes, records, and structs can also supply values.

```csharp
public sealed class AlbumFilter
{
    public int AlbumId { get; init; }
}

public record AlbumFilterRecord(int AlbumId);

Album fromClass = GetAlbum.Query<Album>(cnn, new AlbumFilter { AlbumId = 1 });
Album fromRecord = GetAlbum.Query<Album>(cnn, new AlbumFilterRecord(1));
```

## Struct parameters

```csharp
public struct AlbumFilterStruct
{
    public int AlbumId { get; init; }
}

AlbumFilterStruct filter = new() { AlbumId = 1 };
Album album = GetAlbum.Query<Album, AlbumFilterStruct>(cnn, ref filter);
```

Use the `ref` overload when a large struct should not be copied.

## Null means absent

```csharp
string? title = null;
SearchAlbums.Query<List<Album>>(cnn, new { title });
// @title is absent.
```

A missing required parameter leaves ordinary SQL unchanged. The provider then sees a missing parameter.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title
```

A missing conditional parameter removes the SQL that depends on it.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

See [conditional variables](../conditional-sql/variables.md) for values that decide whether SQL remains in the template.

## Send database NULL

```csharp
ClearTitle.Execute(cnn, new { albumId = 1, title = DBNull.Value });
```

Use `DBNull.Value` when the parameter must exist and contain database `NULL`.

A nullable member can use `[UseDbNull]` when `null` should mean database `NULL` for that member.

```csharp
public sealed class AlbumUpdate
{
    public int AlbumId { get; init; }

    [UseDbNull]
    public string? Title { get; init; }
}

ClearTitle.Execute(cnn, new AlbumUpdate { AlbumId = 1, Title = null });
```

Put `[UseDbNull]` on the type when the rule should apply to every member.

## Ignore empty text

```csharp
public sealed class AlbumTitleSearch
{
    [NotNullOrWhitespace]
    public string? Title { get; init; }
}

SearchAlbums.Query<List<Album>>(cnn, new AlbumTitleSearch { Title = "   " });
```

`[NotNullOrWhitespace]` treats null, empty text, and whitespace as absent.

## Ignore default values

```csharp
public sealed class AlbumYearSearch
{
    [NotDefault]
    public int MinimumYear { get; init; }
}

SearchByYear.Query<List<Album>>(cnn, new AlbumYearSearch { MinimumYear = 0 });
```

`[NotDefault]` treats the member type default value as absent.

## Supply conditional keys from a type

```csharp
public sealed class AlbumReadOptions
{
    [ForBoolCond]
    public bool IncludeYear { get; init; }
}

ReadAlbums.Query<List<DynaObject>>(cnn, new AlbumReadOptions { IncludeYear = true });
```

`[ForBoolCond]` turns the boolean member into a conditional key instead of a database parameter.

```csharp
[UsesBoolConds("IncludeYear")]
public sealed class AlbumReportOptions
{
    public int? ArtistId { get; init; }
}
```

`[UsesBoolConds]` activates the named keys whenever that parameter type is used.

See [conditional markers](../conditional-sql/markers.md) for the SQL controlled by those keys.

## Build values in steps

```csharp
var search = SearchAlbums.StartBuilder();
search.UseWith(new { ArtistId = 7 });
search.UseWith(new { Title = "Blue" });

List<Album> albums = search.Query<List<Album>>(cnn);
```

Use a [builder](builders.md) when application logic decides values over several steps.

## Positional parameters

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

Declare positional variables in provider order and configure their parameter metadata by index.

See [parameter metadata](parameter-metadata.md) for explicit database types and directions. See [parameter customization](../customization/parameters.md) when the built in member rules are not enough.
