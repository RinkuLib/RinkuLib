# Supplying values

## Object members

```csharp
public record AlbumSearch(int ArtistId, string Title);

static readonly QueryCommand SearchAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @ArtistId AND Title = @Title");

List<Album> albums = SearchAlbum.Query<List<Album>>(cnn, new AlbumSearch(7, "Blue"));
```

Public fields and properties supply matching variables. Names match without regard to case. Members that do not belong to the command are ignored.

```csharp
var values = new { ARTISTID = 7, Title = "Blue", IgnoredByThisQuery = 123 };
List<Album> albums = SearchAlbum.Query<List<Album>>(cnn, values);
```

## Struct values

```csharp
public readonly record struct AlbumFilter(int ArtistId, string Title);

AlbumFilter filter = new(7, "Blue");
List<Album> albums = SearchAlbum.Query<List<Album>, AlbumFilter>(cnn, ref filter);
```

The `ref` overload supplies a struct without copying it into another parameter object.

## Dictionary

```csharp
var values = new Dictionary<string, object?>
{
    ["artistId"] = 7,
    ["title"] = "Blue"
};

List<Album> albums = SearchAlbum.Query<List<Album>>(cnn, values);
```

## Application null and database NULL

A `null` application value is absent by default.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = (int?)null });
// SELECT AlbumId AS Id, Title FROM albums
```

`DBNull.Value` supplies an explicit database `NULL`.

```csharp
static readonly QueryCommand ClearTitle = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

ClearTitle.Execute(cnn, new { albumId = 12, title = DBNull.Value });
```

`[UseDbNull]` can apply the same behavior to a nullable member.

```csharp
public record AlbumTitleUpdate(int AlbumId, [property: UseDbNull] string? Title);

static readonly QueryCommand UpdateTitle = new("UPDATE albums SET Title = @Title WHERE AlbumId = @AlbumId");
UpdateTitle.Execute(cnn, new AlbumTitleUpdate(12, null));
```

It can also apply to every member of a parameter type.

```csharp
[UseDbNull]
public sealed class AlbumUpdate
{
    public int? ArtistId { get; init; }
    public string? Title { get; init; }
}
```

## Presence rules

```csharp
public record AlbumSearch([property: NotDefault] int ArtistId, [property: NotNullOrWhitespace] string? Title);

static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@ArtistId AND Title LIKE ?@Title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch(0, "   "));
// SELECT AlbumId AS Id, Title FROM albums
```

`[NotDefault]` treats the member type default as absent. `[NotNullOrWhitespace]` treats null, empty text, and whitespace as absent.

[Custom parameter member rules](../customization/parameter-members.md)

## Boolean conditions from a value source

```csharp
public sealed class AlbumReadOptions
{
    [ForBoolCond]
    public bool IncludeYear { get; init; }
}

static readonly QueryCommand ReadAlbums = new("SELECT AlbumId AS Id, Title /*IncludeYear*/, ReleaseYear FROM albums");

List<DynaObject> rows = ReadAlbums.Query<List<DynaObject>>(cnn, new AlbumReadOptions { IncludeYear = true });
```

`[ForBoolCond]` uses the boolean member as a condition key instead of a database parameter.

A type can activate named conditions whenever the type is supplied.

```csharp
[UsesBoolConds("CurrentOnly")]
public sealed class CurrentAlbumFilter
{
    public int? ArtistId { get; init; }
}

static readonly QueryCommand ReadCurrentAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@ArtistId AND /*CurrentOnly*/IsArchived = 0");

List<Album> albums = ReadCurrentAlbums.Query<List<Album>>(cnn, new CurrentAlbumFilter { ArtistId = 7 });
```

[Conditional markers](../conditional-sql/markers.md)

## Builder sources

```csharp
var builder = SearchAlbums.StartBuilder();
builder.UseWith(new { ArtistId = 7 });
builder.UseWith(new { Title = "Blue%" });

List<Album> albums = builder.Query<List<Album>>(cnn);
```

[Builders](builders.md)

## Positional variables

```csharp
static readonly QueryCommand FindUser = CreateFindUser();

static QueryCommand CreateFindUser()
{
    QueryCommand command = new("SELECT UserId, Name FROM users WHERE UserId = ? AND Status = ?", ["userId", "status"], CommandType.Text);
    command.UpdateParamCache(0, new PositionalDbParamInfo());
    command.UpdateParamCache(1, new PositionalDbParamInfo());
    return command;
}

var builder = FindUser.StartBuilder();
builder.Use(0, 12);
builder.Use(1, "A");
```

[Parameter metadata](parameter-metadata.md) · [Parameter customization](../customization/parameters.md)
