# Supplying values

## An object

Members map to variables by name. A member with no matching variable is ignored.

```csharp
static readonly QueryCommand ByAlbum = new("SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId");

ByAlbum.Query<List<Track>>(cnn, new { AlbumID = 1 }); // matches @albumId case-insensitively
```

The parameter object can also be a class, record, or struct.

```csharp
public sealed class AlbumFilter {
    public int AlbumId { get; init; }
}

public record AlbumFilterRecord(int AlbumId);

public struct AlbumFilterStruct {
    public int AlbumId { get; init; }
}

ByAlbum.Query<List<Track>>(cnn, new AlbumFilter { AlbumId = 1 });
ByAlbum.Query<List<Track>>(cnn, new AlbumFilterRecord(1));
ByAlbum.Query<List<Track>>(cnn, new AlbumFilterStruct { AlbumId = 1 });
```

## Driving optional markers

On a [conditional template](../conditional-sql/index.md), supplying a member activates its marker, leaving it out prunes it.

```csharp
static readonly QueryCommand Search = new("SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = ?@albumId AND Composer = ?@composer");

Search.Query<List<Track>>(cnn, new { albumId = 1 });
// WHERE AlbumId = @albumId
```

A `null` member also counts as not supplied.

```csharp
public sealed class TrackFilter {
    public int? AlbumId { get; init; }
    public string? Composer { get; init; }
}

Search.Query<List<Track>>(cnn, new TrackFilter { AlbumId = 1 });
// WHERE AlbumId = @albumId
```

## Sending NULL

`null` means "not supplied", it never reaches the database. To send a SQL `NULL` as the parameter's value, pass `DBNull.Value`.

```csharp
ClearComposer.Execute(cnn, new { id = 10, composer = DBNull.Value });
// UPDATE tracks SET Composer = @composer ... with @composer = NULL
```

## A builder

Use a builder when C# logic decides what is active.

```csharp
var b = SearchCmd.StartBuilder();
b.Use("@albumId", 1);
if (alsoByComposer)
    b.Use("@composer", "AC/DC");
List<Track> tracks = b.Query<List<Track>>(cnn);
```

`Use(name, value)` supplies a parameter. `Use(name)` activates a comment condition. Both return `true` when the builder contains that key.

```csharp
if (!b.Use("IncludeArtist"))
    throw new InvalidOperationException("This query has no IncludeArtist condition");

static int UpdateRow(QueryBuilder b, DbConnection cnn, int id) {
    if (!b.Use("@id", id))
        throw new InvalidOperationException("This command has no @id, refusing an unkeyed update");
    return b.Execute(cnn);
}
```

The variable character can be supplied separately, which pairs with `nameof`.

```csharp
b.Use('@', nameof(TrackFilter.AlbumId), 1);   // same key as "@AlbumId"
```

UseWith copies a parameter object's usable members into the builder. It uses the same member rules as a
direct parameter-object run, but leaves the values in the builder for later execution.

```csharp
public sealed class TrackFilter {
    public int? AlbumId { get; init; }
    public string? Composer { get; init; }
    [ForBoolCond] public bool IncludeArtist;
}

var filter = new TrackFilter { AlbumId = 1, IncludeArtist = true };
var b = SearchCmd.StartBuilder();
b.UseWith(filter);

List<Track> tracks = b.Query<List<Track>>(cnn);
// @albumId is set, @composer is off, and IncludeArtist is on.
```

Calling UseWith again replaces the builder values. A member that is not usable clears its previous value.

```csharp
b.UseWith(new TrackFilter { AlbumId = 2, Composer = "AC/DC" });
b.UseWith(new TrackFilter());
// all three mapped pieces are now off
```

The rest of the builder surface:

```csharp
var b = SearchCmd.StartBuilder([("@albumId", 1), ("@composer", "AC/DC")]); // start with values

b.Use("@status", "active");
b.Use("IncludeArtist");      // activate a condition
b.UnUse("IncludeArtist");    // deactivate a condition, the counterpart of Use(name)
b.Remove("@composer");       // clear any key, variable or condition
b.Reset();                    // clear everything

string sql = b.GetQueryText();   // the SQL this state would produce, handy for debugging
```

## A builder bound to one DbCommand

`StartBuilder(cmd)` binds the builder to the `DbCommand` you provide and updates only what changes between runs. Because the command already has its connection and transaction, the execution methods need neither, and you remain responsible for disposing the command after the batch.

```csharp
using var sqlCmd = cnn.CreateCommand();
var batch = InsertPlaylist.StartBuilder(sqlCmd);

foreach (var name in names) {
    batch.Use("@name", name);
    batch.Execute();
}
```

The builder can be reused by setting each value before execution:

```csharp
using var sqlCmd = cnn.CreateCommand();
var batch = InsertRow.StartBuilder(sqlCmd);
batch.Use("id", 1);
batch.Use("foo", 2);
await batch.ExecuteAsync(token);
batch.Use("id", 3);
batch.Use("foo", 4);
await batch.ExecuteAsync(token);
```

UseWith also works on the bound builder. It clears the live command, copies the new object, and processes its
parameters immediately.

```csharp
using var sqlCmd = cnn.CreateCommand();
var batch = InsertRow.StartBuilder(sqlCmd);
batch.UseWith(new { id = 1, foo = 2 });
batch.Execute();

batch.UseWith(new { id = 3, foo = 4 });
batch.Execute();
```

## Struct parameter objects

```csharp
AlbumFilterStruct filter = new() { AlbumId = 2 };

cmd.Query<List<Track>>(cnn, (object)filter);                // works with any parameter object
cmd.Query<List<Track>, AlbumFilterStruct>(cnn, filter);     // use this form for a struct
cmd.Query<List<Track>, AlbumFilterStruct>(cnn, ref filter); // use this form for a large struct
```

The same forms work with `Execute`, `ExecuteScalar`, and `StreamQueryAsync`.

```csharp
AlbumFilterStruct filter = new() { AlbumId = 2 };

b.UseWith((object)filter);         // any parameter object
b.UseWith<AlbumFilterStruct>(filter); // use this form for a struct
b.UseWith(ref filter);                // use this form for a large struct
```

`QueryCommand` caches the generated accessor for each parameter type. You can clear the direct accessor, the
`UseWith` accessor, or both.

```csharp
ByAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Direct);
ByAlbum.InvalidateParameterAccessor(typeof(AlbumFilter), ParameterAccessorKinds.Both);
```

Call `GetCachedParameterAccessors()` when you need the parameter types currently cached by the command. Parameter
accessors are separate from row-parser caching. See [parser invalidation](../mapping/parsers.md#invalidation) for the
row-parser APIs.

## Member rules

Start with the normal rule. A member is used when its value is not `null`.

```csharp
public sealed class TrackSearch {
    public int? AlbumId { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new TrackSearch { AlbumId = 1 });
// @albumId is supplied

SearchCmd.Query<List<Track>>(cnn, new TrackSearch());
// @albumId is not supplied
```

Use `NotNullOrWhitespace` when an empty string should also stay out.

```csharp
public sealed class ArtistSearch {
    [NotNullOrWhitespace] public string? Composer { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new ArtistSearch { Composer = "  " });
// @composer is not supplied

SearchCmd.Query<List<Track>>(cnn, new ArtistSearch { Composer = "AC/DC" });
// @composer is supplied
```

Use `UseDbNull` when a null member must be sent as a SQL `NULL`.

```csharp
public sealed class UpdateTrack {
    public int Id { get; init; }
    [UseDbNull] public string? Composer { get; init; }
}

var update = new QueryCommand("UPDATE tracks SET Composer = @Composer WHERE TrackId = @Id");
update.Execute(cnn, new UpdateTrack { Id = 10, Composer = null });
// @Composer is sent as SQL NULL
```

Put `UseDbNull` on the type when that is the default for every member. A member attribute still wins.

```csharp
[UseDbNull]
public sealed class UpdateTrack {
    public int Id { get; init; }
    public string? Composer { get; init; }
    [NotNullOrWhitespace] public string? Name { get; init; }
}

update.Execute(cnn, new UpdateTrack { Id = 10, Composer = null, Name = null });
// @Composer is sent as SQL NULL
// @Name is not supplied because its member rule wins
```

Use `NotDefault` when the default value should stay out.

```csharp
public sealed class PriceSearch {
    [NotDefault] public decimal MinPrice { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new PriceSearch { MinPrice = 0 });
// @minPrice is not supplied

SearchCmd.Query<List<Track>>(cnn, new PriceSearch { MinPrice = 9.99m });
// @minPrice is supplied
```

Use `ForBoolCond` for a boolean [condition key](../conditional-sql/conditional-markers.md#custom-keys), not a parameter.

```csharp
static readonly QueryCommand SearchCmd = new("""
    SELECT TrackId, Name, /*IncludeArtist*/ArtistId
    FROM tracks
    """);

public sealed class TrackSearch {
    [ForBoolCond] public bool IncludeArtist { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new TrackSearch { IncludeArtist = false });
// SELECT TrackId, Name FROM tracks

SearchCmd.Query<List<Track>>(cnn, new TrackSearch { IncludeArtist = true });
// SELECT TrackId, Name, ArtistId FROM tracks
```

Use `UsesBoolConds` when every use of a type turns on the same condition keys.

```csharp
static readonly QueryCommand SearchCmd = new("""
    SELECT TrackId, Name, /*WithYear*/Milliseconds
    FROM tracks
    """);

[UsesBoolConds("WithYear")]
public sealed class ReportSearch {
    public int? AlbumId { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new ReportSearch { AlbumId = 1 });
// SELECT TrackId, Name, Milliseconds FROM tracks
// @albumId is supplied too
```

The same rules work with `UseWith`.

```csharp
var b = SearchCmd.StartBuilder();
b.UseWith(new TrackSearch { IncludeArtist = true });

string sql = b.GetQueryText();
// SELECT TrackId, Name, ArtistId FROM tracks
```

For a rule of your own, see [custom member rules](custom-member-rules.md).

## Positional SQL

The manual-variable constructor can drive SQL whose provider binds parameters by position. The SQL is kept
unchanged, and the variables are created in the order supplied to the constructor. Use the built-in
`PositionalDbParamInfo` for the parameter slots. The names `"param0"` and `"param1"` only identify those slots inside
`QueryCommand`, they can be anything.

```csharp
using System.Data;
using System.Data.Common;
using Rinku.Querying;
using Rinku.Querying.Defaults;

var positional = new QueryCommand(
    "SELECT * FROM Users WHERE Id = ? AND Status = ?",
    ["param0", "param1"],
    CommandType.Text);

positional.UpdateParamCache(0, new PositionalDbParamInfo());
positional.UpdateParamCache(1, new PositionalDbParamInfo());

var b = positional.StartBuilder();
b.Use(0, 7);
b.Use(1, "active");
var users = b.Query<List<User>>(cnn);
// The provider receives the parameters in the order: 7, "active".
```
