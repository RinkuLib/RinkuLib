# Supplying values

A run needs the values for this call. There are two ways to hand them over, an object whose members carry them, or a builder you set in C#. Both end in the same execution methods.

## An object

Members map to variables by name, case-insensitive. A member with no matching variable is ignored.

```csharp
static readonly QueryCommand ByAlbum = new(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId");

ByAlbum.Query<List<Track>>(cnn, new { albumId = 1 });
```

Anonymous objects, records, and DTOs all work.

## Driving optional markers

On a [conditional template](../conditional-sql/index.md), supplying a member activates its marker, leaving it out prunes it.

```csharp
static readonly QueryCommand Search = new(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = ?@albumId AND Composer = ?@composer");

Search.Query<List<Track>>(cnn, new { albumId = 1 });
// WHERE AlbumId = @albumId
```

With a typed object, a `null` member counts as not supplied. A filter type is often just nullable fields.

```csharp
public record TrackFilter(int? AlbumId, string? Composer);

Search.Query<List<Track>>(cnn, new TrackFilter(AlbumId: 1, Composer: null));
// WHERE AlbumId = @albumId
```

## Sending NULL

`null` means "not supplied", it never reaches the database. To send a SQL `NULL` as the parameter's value, pass `DBNull.Value`.

```csharp
ClearComposer.Execute(cnn, new { id = 10, composer = DBNull.Value });
// UPDATE tracks SET Composer = @composer ... with @composer = NULL
```

## When null is not the signal

Attributes adjust when a member counts as supplied, or what it drives.

```csharp
public record TrackSearch(
    int? AlbumId,                                        // used when not null
    [property: NotNullOrWhitespace] string? Composer,    // used when not null and not blank
    [property: NotDefault] int MinPrice)                 // used when not 0
{
    [ForBoolCond] public bool IncludeArtist;             // drives a /*IncludeArtist*/ condition
}

var tracks = SearchCmd.Query<List<Track>>(cnn, new TrackSearch(1, "  ", 0) { IncludeArtist = true });
// Composer is blank and MinPrice is default, so only @AlbumId and the IncludeArtist condition are active.
```

- `[NotNullOrWhitespace]` on a string member, used only when it has content.
- `[NotDefault]` on any member, used only when it is not the type's default.
- `[ForBoolCond]` on a `bool` member drives a comment condition key (see [conditional markers](../conditional-sql/conditional-markers.md)) instead of a parameter.
- `[UsesBoolConds("Key1", "Key2")]` on the type activates the named condition keys whenever this object is used.

```csharp
[UsesBoolConds("Year")]
public record ReportFilter([property: NotDefault] int DeptId);
// every call with a ReportFilter also turns on the "Year" condition
```

These attributes are implementations of one base, `AccessorEmiterHandler`. Deriving from it gives your own attribute the same two controls, when a member counts as supplied, and what value is read from it. `[NotNullOrWhitespace]` is the smallest one to read as a reference.

## A builder

Use a builder when C# logic decides what is active.

```csharp
var b = SearchCmd.StartBuilder();
b.Use("@albumId", 1);
if (alsoByComposer)
    b.Use("@composer", "AC/DC");
List<Track> tracks = b.Query<List<Track>>(cnn);
```

`Use(name, value)` stores the value and keeps the key's footprint in the SQL. `Use(name)` with no value activates a comment condition. Both return `bool`, whether the command has that key and the bind landed.

That return matters when code receives a builder without knowing which template is behind it. A shared method that runs updates can refuse to run one it cannot key to a row.

```csharp
static int UpdateRow(QueryBuilder b, DbConnection cnn, int id) {
    if (!b.Use("@id", id))
        throw new InvalidOperationException("This command has no @id, refusing an unkeyed update");
    return b.Execute(cnn);
}
```

Throwing is one reaction. Binding an alternative key or skipping dependent work fit the same way.

An overload takes the variable character separately from the name, which pairs with `nameof` to remove the magic string entirely.

```csharp
b.Use('@', nameof(TrackFilter.AlbumId), 1);   // same key as "@AlbumId"
```

The rest of the surface:

```csharp
var b = SearchCmd.StartBuilder([("@albumId", 1), ("@composer", "AC/DC")]); // start with values

b.UseWith(filterObject);     // read an object into the builder, then adjust
b.Use("IncludeArtist");      // activate a condition
b.UnUse("IncludeArtist");    // deactivate a condition, the counterpart of Use(name)
b.Remove("@composer");       // clear any key, variable or condition
b.Reset();                   // clear everything

string sql = b.GetQueryText();   // the SQL this state would produce, handy for debugging
```

## A builder bound to one DbCommand

`StartBuilder(cmd)` returns a builder that owns a `DbCommand` and reconfigures only what changes between runs. Its execution methods take no connection or transaction, the command already has them.

```csharp
using var sqlCmd = new SqlCommand();
var batch = InsertPlaylist.StartBuilder(sqlCmd);

foreach (var name in names) {
    batch.Use("@name", name);
    batch.Execute(cnn);
}
```

`UseWith` works here too, so a batch can be driven by objects:

```csharp
var batch = InsertRow.StartBuilder(cnn.CreateCommand());
batch.UseWith(new { id = 1, foo = 2 });
await batch.ExecuteAsync();
batch.UseWith(new { id = 3, foo = 4 });
await batch.ExecuteAsync();
```

## Avoiding boxing

The parameter-object overloads come in three forms.

```csharp
cmd.Query<T>(cnn, object? parametersObj);          // reflective, anonymous objects and DTOs
cmd.Query<T, TObj>(cnn, TObj parametersObj);       // generic, no boxing for struct holders
cmd.Query<T, TObj>(cnn, ref TObj parametersObj);   // ref, for large structs
```

The same three forms exist on `Execute`, `ExecuteScalar`, `StreamQueryAsync`, and `UseWith`.
