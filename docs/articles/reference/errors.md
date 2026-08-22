# Errors

Every Rinku exception carries a code. The code prefixes the message and its `HelpLink` identifies the matching error entry.

```csharp
try {
    Album album = GetAlbum.Query<Album>(cnn);
}
catch (RinkuException error) {
    logger.LogError("{Code} {Message} {Help}", error.Code, error.Message, error.HelpLink);
}
```

Catch one family or match one code.

```csharp
catch (RinkuReadException error)
    when (error.Code == ErrorCodes.NoRows) {
    return null;
}
```

| Family | Codes | Raised while |
| --- | --- | --- |
| `RinkuTemplateException` | `RINKU1###` | Reading a query template. |
| `RinkuBindingException` | `RINKU2###` | Preparing an execution command. |
| `RinkuMappingException` | `RINKU3###` | Building a parser for a type and schema. |
| `RinkuReadException` | `RINKU4###` | Reading through a parser. |
| `RinkuConfigurationException` | `RINKU5###` | Configuring mapping or value behavior. |
| `RinkuTrackingException` | `RINKU6###` | Copying or editing a tracked value. |
| `RinkuInternalException` | `RINKU9###` | Checking an internal invariant. |

Every exception family in the table derives from `RinkuException`.

## Template errors

### RINKU1001 query too short

The template contains fewer than two characters.

```csharp
var command = new QueryCommand("x"); // RINKU1001
```

### RINKU1002 unclosed comment

```sql
SELECT /*IncludeTitle Title FROM albums
```

Close the marker or literal comment.

```sql
SELECT /*IncludeTitle*/Title FROM albums
SELECT /*~ application note */Title FROM albums
```

### RINKU1003 empty condition key

```sql
SELECT /**/Title FROM albums
SELECT /*Visible&*/Title FROM albums
```

Both sides of an `&` or `|` operator need a key.

```sql
SELECT /*Visible*/Title FROM albums
SELECT /*Visible&Published*/Title FROM albums
```

### RINKU1004 unknown handler suffix

```sql
SELECT AlbumId AS Id FROM albums WHERE AlbumId IN (@albumIds_Q)
```

Use a registered suffix. Collection expansion uses `_X`.

```sql
SELECT AlbumId AS Id FROM albums WHERE AlbumId IN (@albumIds_X)
```

See [value handlers](../conditional-sql/handlers.md) for the supported suffix handler flow.

### RINKU1005 condition variable not in the query

```sql
SELECT AlbumId AS Id FROM albums WHERE /*@missing*/IsArchived = 0
```

A marker beginning with the variable character must name a variable that appears in the template.

```sql
SELECT AlbumId AS Id FROM albums WHERE /*@albumId*/AlbumId = @albumId
```

A custom key needs no matching variable.

```sql
SELECT AlbumId AS Id FROM albums WHERE /*Current*/IsArchived = 0
```

### RINKU1006 unbalanced scope

```sql
SELECT AlbumId) FROM albums
SELECT CASE WHEN IsArchived = 1 THEN 0 ELSE 1 END END FROM albums
```

A closing parenthesis or `END` has no matching open scope.

### RINKU1007 scope too deep

Parentheses, `CASE`, and `BEGIN` share a maximum nesting depth of 63.

```text
64 nested scopes -> RINKU1007
```

### RINKU1008 projection only construct

```sql
SELECT AlbumId AS Id!, Title FROM albums
```

`!` after a projected column is valid only in `?SELECT`.

```sql
?SELECT AlbumId AS Id!, Title FROM albums
```

See [dynamic projection](../conditional-sql/dynamic-projection.md) for projection syntax and generated SQL.

## Binding errors

### RINKU2001 no connection

```csharp
DbCommand command = CreateUnboundCommand();
command.CommandText = "SELECT AlbumId AS Id FROM albums";

Album album = parser.Query(command); // RINKU2001
```

Create the command from a connection or assign its connection before execution.

```csharp
DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id FROM albums";

Album album = parser.Query(command);
```

### RINKU2002 required handler value

```csharp
static readonly QueryCommand ById = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_X)");

List<Album> albums = ById.Query<List<Album>>(cnn); // RINKU2002
```

Supply a nonempty value or make the handler conditional.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (?@albumIds_X)
```

### RINKU2003 handler value type

```csharp
values.Use("@skip", "not a number");
// RINKU2003 when the template uses @skip_N.
```

```csharp
values.Use("@skip", 46);
values.Use("@skip", "46");
```

### RINKU2004 invalid parameter at index

A caller created `IDbCommand` returned something that is not an `IDbDataParameter` from its parameter collection. Normal providers reject that item when it is added.

```text
command.Parameters[index] -> object that is not IDbDataParameter -> RINKU2004
```

### RINKU2005 value not set

```csharp
var handler = MultiVariableHandler.Build("@albumIds");
object? state = null;

handler.Update(command, ref state, new[] { 2, 5 }); // RINKU2005
```

`Update` requires state previously created by `SaveUse`. Builders perform those operations in order. This error normally comes from driving a `SpecialHandler` directly.

### RINKU2006 type carries no size

```csharp
SizedDbParamCache.Get(DbType.Int32, 100); // RINKU2006
```

Use size only with text, binary, XML, and fixed length database types.

## Mapping errors

### RINKU3001 no parser for the schema

The returned columns cannot satisfy any construction path for the requested type.

For example, the returned names may not match the requested type.

```sql
SELECT AlbumId, AlbumTitle FROM albums
```

```csharp
public record Album(int Id, string Title);
```

An SQL alias can adapt those names without changing the type.

```sql
SELECT AlbumId AS Id, AlbumTitle AS Title FROM albums
```

When the SQL cannot change, the type can accept the database names instead.

```csharp
public record Album([Alt("AlbumId")] int Id, [Alt("AlbumTitle")] string Title);
```

A returned column may also have an incompatible type.

```csharp
public record Session(int Id, Guid Token);
// RINKU3001 when Token is an integer column.
```

The same error occurs when a nested type has not been registered.

```csharp
public record Artist(int Id, string Name);
public record Album(int Id, string Title, Artist Artist);
```

```csharp
public record Artist(int Id, string Name) : IDbReadable;
```

See [objects](../mapping/objects.md), [names](../mapping/names.md), and [registration](../mapping/registration.md).

### RINKU3002 missing group boundary

```csharp
public record Report(List<int> Rows, int Total);

Report report = GetReport.Query<Report>(cnn); // RINKU3002
```

A multi row value has no usable boundary separating one complete `Report` from the next. Put stable scalar values before the first multi row value or configure a [grouping rule](../mapping/grouping.md).

### RINKU3003 group key matched no column

```csharp
throw new RinkuConfigurationException(ErrorCodes.GroupKeyUnmapped, "the required AccountId key matched no column");
```

A grouping rule raises this when its key is mandatory and the schema cannot supply it. A rule that wants Rinku to try the next grouping option returns `null` instead.

### RINKU3004 conflicting grouping rules

```csharp
public class Batch {
    [GroupKeyMethod(nameof(ByWindow))]
    public Batch([GroupKey] int id, List<string> items) { }

    public static (bool Same, int Next) ByWindow(int stored, int current) => default;
}
```

The constructor declares both equality key and method grouping at the same level. Keep one rule family there. See [grouping precedence](../mapping/grouping.md).

## Reading errors

### RINKU4001 no results

```csharp
Album album = FindAlbum.Query<Album>(cnn); // RINKU4001
```

Choose a shape that represents absence when no result is valid.

```csharp
Optional<Album> album = FindAlbum.Query<Optional<Album>>(cnn);
List<Album> albums = FindAlbum.Query<List<Album>>(cnn);
```

### RINKU4002 result shape refused the results

```csharp
Album album = GetAlbum.Query<Single<Album>>(cnn);
// RINKU4002 when a second complete Album exists.
```

`Single<T>` and the single or default shapes reject a second result. See [result shapes](../running-queries/result-shapes.md).

### RINKU4003 database NULL not allowed

```csharp
public record Album(int Id, int ReleaseYear);
// NULL ReleaseYear -> RINKU4003
```

```csharp
public record Album(int Id, int? ReleaseYear);
```

Reference types accept database `NULL` by default. `[NotNull]` makes a reference slot reject it. See [database NULL](../mapping/nulls.md).

### RINKU4004 cannot convert

```csharp
static readonly QueryCommand GetTitle = new("SELECT Title FROM albums WHERE AlbumId = @albumId");

int title = GetTitle.ExecuteScalar<int>(cnn, new { albumId = 12 }); // RINKU4004
```

Ask for the returned type or change the returned value.

```csharp
string title = GetTitle.ExecuteScalar<string>(cnn, new { albumId = 12 });
```

When a previously accepted conversion rejects one particular value, its own exception remains visible. Invalid `Guid` text can therefore raise `FormatException` directly.

### RINKU4005 cannot read a dynamic column

```csharp
Version invalidId = row.Get<Version>("Id"); // RINKU4005 for an integer column
int id = row.Get<int>("Id");
```

See [dynamic rows](../mapping/dynamic-rows.md) for runtime row access and name comparison.

## Configuration errors

### RINKU5001 type not usable by this parsing info

A `TypeParsingInfo` was asked to handle a type it does not support. Custom implementations report this from their type validation. See [type registrations](../customization/type-registration.md) for the user facing registration path.

### RINKU5002 construction shape not usable

An offered constructor or factory has the wrong static, generic, parameter, or return shape.

```csharp
static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);
}
```

See [constructors and factories](../mapping/construction-paths.md#add-a-constructor-or-factory) for supported factory shapes and registration.

### RINKU5003 unusable member

```csharp
public class Row {
    public int Id { get; }
}

TypeParsingInfo.GetOrAdd<Row>().AddMember(typeof(Row).GetProperty(nameof(Row.Id))!); // RINKU5003
```

Added members must be writable fields, settable properties, or usable setter methods.

### RINKU5004 target type mismatch

```csharp
TypeParsingInfo info = TypeParsingInfo.GetOrAdd<Album>();
info.AddPossibleConstruction(typeof(Payment).GetConstructors()[0]); // RINKU5004
```

The path builds a different target type.

### RINKU5005 construction from a foreign generic type

Move an open generic factory from a generic host to a nongeneric host.

```csharp
static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);
}
```

### RINKU5006 attribute on the wrong member type

```csharp
public class Options {
    [ForBoolCond] public int IncludeDeleted { get; init; }
}
```

```csharp
public class Options {
    [ForBoolCond] public bool IncludeDeleted { get; init; }
}
```

### RINKU5007 operation unsupported for this type

```csharp
JsonSerializer.Deserialize<DynaObject>("{}", options); // RINKU5007
```

`DynaObject` gets its shape from a live result schema. It can be serialized but cannot be reconstructed without that schema.

## Tracking errors

### RINKU6001 no copy strategy

The tracking materializer could not create a usable runtime shape for the requested
item. Check the tracking contract and ensure every required member has a supported
source or explicit runtime binding. See the [tracking overview](../tracking/index.md).

### RINKU6002 copy method not usable

The requested tracking member has a source method or property that does not match its declared value type. Bind a compatible member or provide a matching runtime capability. See [runtime tracking](../tracking/runtime.md) for member configuration.

### RINKU6003 no current value

A tracked slot was read for display while holding no current value.

```text
tracked slot has no current value -> display reads the slot -> RINKU6003
```

### RINKU6004 no factory for a new item

```csharp
IBindingList binding = tracked;
binding.AddNew(); // RINKU6004
```

Set a new item factory or handle `AddingNew` before calling `AddNew`.

## Internal errors

### RINKU9001 internal invariant

This reports a library bug rather than an invalid application call. Report it with the stack trace, query template, target type, and result schema when available. Use the [RinkuLib issue tracker](https://github.com/RinkuLib/RinkuLib/issues) when the error is reproducible.
