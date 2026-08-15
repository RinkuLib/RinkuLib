# Value handlers

A suffix changes how a supplied value appears in the generated SQL.

```sql
@value_N
@value_S
@value_R
@value_X
```

The suffix is not part of the supplied name.

```csharp
var values = command.StartBuilder();
values.Use("@value", 46);
```

## Write a number with `_N`

`_N` writes a numeric value into the SQL text using invariant culture.

```csharp
static readonly QueryCommand PageAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET @skip_N ROWS FETCH NEXT @take_N ROWS ONLY");

List<Album> albums = PageAlbums.Query<List<Album>>(cnn, new { skip = 20, take = 10 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY
```

Signed and unsigned integers, floating-point types, `decimal`, and supported newer numeric types are accepted.

```csharp
static readonly QueryCommand NumericValues = new("SELECT @integer_N AS IntegerValue, @fraction_N AS FractionValue");

var values = new { integer = 46u, fraction = 1.5m };
```

```sql
SELECT 46 AS IntegerValue, 1.5 AS FractionValue
```

A nullable numeric with a value arrives as its underlying numeric type.

```csharp
int? take = 10;
List<Album> albums = PageAlbums.Query<List<Album>>(cnn, new { skip = 0, take });
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY
```

Boolean values become `1` or `0`.

```csharp
static readonly QueryCommand NumericFlag = new("SELECT @enabled_N AS Enabled");

int enabled = NumericFlag.Query<int>(cnn, new { enabled = true });
```

```sql
SELECT 1 AS Enabled
```

Enums become their underlying integral value.

```csharp
public enum AlbumState { Draft = 1, Published = 2 }

static readonly QueryCommand NumericState = new("SELECT @state_N AS State");

int state = NumericState.Query<int>(cnn, new { state = AlbumState.Published });
```

```sql
SELECT 2 AS State
```

A numeric string is accepted when it converts to `decimal` using invariant culture.

```csharp
int value = NumericFlag.Query<int>(cnn, new { enabled = "46" });
```

```sql
SELECT 46 AS Enabled
```

Zero is a supplied value unless a supplying-values rule such as `[NotDefault]` marks it absent.

## Write a quoted value with `_S`

`_S` accepts any non-null value, converts it to text using invariant culture, and writes it inside single quotes.

```csharp
static readonly QueryCommand ArtistByName = new("SELECT ArtistId AS Id, Name FROM artists WHERE Name = @name_S");

Artist artist = ArtistByName.Query<Artist>(cnn, new { name = "Queen" });
```

```sql
SELECT ArtistId AS Id, Name FROM artists WHERE Name = 'Queen'
```

A single quote inside the value is escaped by doubling it.

```csharp
Artist artist = ArtistByName.Query<Artist>(cnn, new { name = "O'Brien" });
```

```sql
SELECT ArtistId AS Id, Name FROM artists WHERE Name = 'O''Brien'
```

A non-string value is converted to text before quoting.

```csharp
static readonly QueryCommand QuotedValue = new("SELECT @value_S AS Value");

string value = QuotedValue.Query<string>(cnn, new { value = 46 });
```

```sql
SELECT '46' AS Value
```

## Write trusted SQL with `_R`

`_R` calls `ToString()` on any non-null value and writes the result without escaping.

```csharp
static readonly QueryCommand OrderedAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY @orderBy_R");

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { orderBy = "Title DESC" });
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY Title DESC
```

Only pass application-controlled values to `_R`. A user value becomes executable SQL text.

```csharp
string orderBy = request.Query["orderBy"];

List<Album> albums = OrderedAlbums.Query<List<Album>>(cnn, new { orderBy });
// Unsafe: orderBy came from the request.
```

## Expand a collection with `_X`

`_X` creates one database parameter per item, numbered from one.

```csharp
static readonly QueryCommand Genres = new("SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_X) OR ParentGenreId IN (@ids_X)");

List<Genre> genres = Genres.Query<List<Genre>>(cnn, new { ids = new[] { 2, 5 } });
```

```sql
SELECT GenreId AS Id, Name FROM genres WHERE GenreId IN (@ids_1, @ids_2) OR ParentGenreId IN (@ids_1, @ids_2)
```

Repeated occurrences reuse the same generated parameters.

The [collection expansion guide](collections.md) covers empty and conditional collections.

## Handlers do not make SQL optional

An unmarked handler value is required.

```csharp
static readonly QueryCommand PageAlbums = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET @skip_N ROWS");

List<Album> albums = PageAlbums.Query<List<Album>>(cnn);
// RINKU2002: @skip is required by the active _N handler.
```

Add `?` when absence should remove the surrounding footprint.

```csharp
static readonly QueryCommand OptionalPage = new("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId OFFSET ?@skip_N ROWS");

List<Album> albums = OptionalPage.Query<List<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId
```

The same composition applies to `_S`, `_R`, and `_X`.

## Missing values

`null` is absent before it reaches a handler. A required `_N`, `_S`, or `_R` value therefore raises `RINKU2002` during SQL generation.

```csharp
List<Album> albums = OptionalPage.Query<List<Album>>(cnn, new { skip = (int?)null });
```

```sql
SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId
```

An empty `_X` collection is also absent. Without `?`, it raises `RINKU2002` instead of producing `IN ()`.

```csharp
List<Genre> genres = Genres.Query<List<Genre>>(cnn, new { ids = Array.Empty<int>() });
// RINKU2002: @ids is required by the active _X handler.
```

A value that `_N` cannot convert raises `RINKU2003` during SQL generation.

```csharp
int value = NumericFlag.Query<int>(cnn, new { enabled = "not a number" });
// RINKU2003: the _N handler cannot convert the supplied value.
```

`_N`, `_S`, and `_R` emit SQL text without creating database parameters. `_X` creates the expanded parameters.

You can add your own suffixes. See [conditional SQL customization](../customization/conditional-sql.md).
