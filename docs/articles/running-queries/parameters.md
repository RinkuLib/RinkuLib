# Supplying values

A run needs the values for this call. There are two ways to hand them over, an object whose members carry them, or a builder you set in C#. Both end in the same execution methods.

## An object

Members map to variables by name, case-insensitive. A member with no matching variable is ignored.

```csharp
static readonly QueryCommand ByAlbum = new(
    "SELECT TrackId AS Id, Name FROM tracks WHERE AlbumId = @albumId");

ByAlbum.Query<List<Track>>(cnn, new { AlbumID = 1 }); // matches @albumId case-insensitively
```

The object can be an anonymous type, an ordinary class, a record, or a struct. A record is simply one possible C# type declaration; it can also serve as a DTO when the application uses it to carry data.

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

When the parameter type is external, register the same handler at setup time:

```csharp
sealed class BoolConditionHandler : AccessorEmiterHandler
{
    public override void HandleEmit(char varChar, IAccessorEmiter?[] usage, IAccessorEmiter?[] values,
        Type type, MemberInfo? member, Mapper mapper)
    {
        int index = mapper.GetIndex(member!.Name);
        if (index < 0) return;
        usage[index] = new MemberCondUsageEmitter(type, member);
        values[index] = BoxedBasicValueEmitter.TrueValue;
    }
}

sealed class ExternalFilter
{
    public bool Include { get; set; }
}

var command = new QueryCommand("SELECT * FROM users WHERE /*Include*/active = 1");
command.RegisterAccessorHandlers<ExternalFilter>(new(
    typeof(ExternalFilter).GetProperty(nameof(ExternalFilter.Include))!,
    new BoolConditionHandler()));
```

The registration has the same scope as an attribute on the member. Register it before the command is used.

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

UseWith copies a parameter object's usable members into the builder. It uses the same member rules as a
direct parameter-object run, but leaves the values in the builder for later execution.

```csharp
public record TrackFilter(int? AlbumId, string? Composer) {
    [ForBoolCond] public bool IncludeArtist;
}

var filter = new TrackFilter(1, null) { IncludeArtist = true };
var b = SearchCmd.StartBuilder();
b.UseWith(filter);

List<Track> tracks = b.Query<List<Track>>(cnn);
// @albumId is set, @composer is off, and IncludeArtist is on.
```

Calling UseWith again replaces the builder values. A member that is not usable clears its previous value.

```csharp
b.UseWith(new TrackFilter(2, "AC/DC") { IncludeArtist = false });
b.UseWith(new TrackFilter(null, null));
// all three mapped pieces are now off
```

UseWith accepts object, generic, and ref forms. The ref form avoids copying a large struct.

```csharp
b.UseWith(new { albumId = 1 });

TrackFilter filter = new(2, "AC/DC");
b.UseWith(ref filter);
```

Throwing is one reaction. Binding an alternative key or skipping dependent work fit the same way.

## Positional SQL

The manual-variable constructor can drive SQL whose provider binds parameters by position. The SQL is kept
unchanged, and the variables are created in the order supplied to the constructor.

```csharp
using System.Data;

var positional = new QueryCommand(
    "SELECT * FROM Users WHERE Id = ? AND Status = ?",
    ["0", "1"],
    CommandType.Text);

// Provider-specific implementation supplied by the application.
positional.UpdateParamCache("@0", new PositionalParamInfo());
positional.UpdateParamCache("@1", new PositionalParamInfo());

var b = positional.StartBuilder();
b.Use("@0", 7);
b.Use("@1", "active");
var users = b.Query<List<User>>(cnn);
// The provider receives the parameters in the order: 7, "active".
```

`PositionalParamInfo` is responsible for creating the parameter form required by the provider. This keeps
provider-specific behavior outside Rinku while leaving the SQL and registration under application control.

An overload takes the variable character separately from the name, which pairs with `nameof` to remove the magic string entirely.

```csharp
b.Use('@', nameof(TrackFilter.AlbumId), 1);   // same key as "@AlbumId"
```

The rest of the surface:

```csharp
var b = SearchCmd.StartBuilder([("@albumId", 1), ("@composer", "AC/DC")]); // start with values

b.Use("@status", "active");
b.Use("IncludeArtist");      // activate a condition
b.UnUse("IncludeArtist");    // deactivate a condition, the counterpart of Use(name)
b.Remove("@composer");       // clear any key, variable or condition
b.Reset();                   // clear everything

string sql = b.GetQueryText();   // the SQL this state would produce, handy for debugging
```

## A builder bound to one DbCommand

`StartBuilder(cmd)` returns a builder that owns a `DbCommand` and reconfigures only what changes between runs. Its execution methods take no connection or transaction, the command already has them.

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
var batch = InsertRow.StartBuilder(cnn.CreateCommand());
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
var batch = InsertRow.StartBuilder(cnn.CreateCommand());
batch.UseWith(new { id = 1, foo = 2 });
batch.Execute();

batch.UseWith(new { id = 3, foo = 4 });
batch.Execute();
```

## Avoiding boxing

The parameter-object overloads come in three forms.

```csharp
cmd.Query<T>(cnn, object? parametersObj);          // reflective, any object or struct with readable members
cmd.Query<T, TObj>(cnn, TObj parametersObj);       // generic, no boxing for struct holders
cmd.Query<T, TObj>(cnn, ref TObj parametersObj);   // ref, for large structs
```

The same parameter forms exist on `Execute`, `ExecuteScalar`, and `StreamQueryAsync`.

`UseWith` has the same object, generic, and ref forms on both builder types.
