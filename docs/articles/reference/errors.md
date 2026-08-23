# Errors

Every Rinku exception has a code. The code is also available through `RinkuException.Code` and the matching documentation entry is exposed through `HelpLink`.

```csharp
public record Album(int Id, string Title);
static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

try
{
    Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
}
catch (RinkuException error)
{
    logger.LogError("{Code} {Message} {Help}", error.Code, error.Message, error.HelpLink);
}
```

A family can be caught directly and one code can be matched.

```csharp
catch (RinkuReadException error) when (error.Code == ErrorCodes.NoRows)
{
    return null;
}
```

| Family | Codes |
| --- | --- |
| `RinkuTemplateException` | `RINKU1###` |
| `RinkuBindingException` | `RINKU2###` |
| `RinkuMappingException` | `RINKU3###` |
| `RinkuReadException` | `RINKU4###` |
| `RinkuConfigurationException` | `RINKU5###` |
| `RinkuTrackingException` | `RINKU6###` |
| `RinkuInternalException` | `RINKU9###` |

Each family derives from `RinkuException`.

## Template errors

### RINKU1001 query too short

```csharp
QueryCommand command = new("x");
// RINKU1001
```

The query template needs at least two characters.

### RINKU1002 unclosed comment

```sql
SELECT /*IncludeTitle Title FROM albums
```

```sql
SELECT /*IncludeTitle*/Title FROM albums
SELECT /*~ application note */Title FROM albums
```

[Template syntax](../conditional-sql/template-syntax.md)

### RINKU1003 empty condition key

```sql
SELECT /**/Title FROM albums
SELECT /*Visible&*/Title FROM albums
```

```sql
SELECT /*Visible*/Title FROM albums
SELECT /*Visible&Published*/Title FROM albums
```

Each condition operand needs a key.

[Markers](../conditional-sql/markers.md)

### RINKU1004 unknown handler suffix

```sql
SELECT AlbumId AS Id FROM albums WHERE AlbumId IN (@albumIds_Q)
```

```sql
SELECT AlbumId AS Id FROM albums WHERE AlbumId IN (@albumIds_X)
```

`_X` is the built in collection expansion suffix. Application handlers can register other suffixes.

[Value handlers](../conditional-sql/handlers.md) · [Custom handlers](../customization/conditional-sql.md)

### RINKU1005 condition variable not in the query

```sql
SELECT AlbumId AS Id FROM albums WHERE /*@missing*/IsArchived = 0
```

A variable marker must name a variable in the template.

```sql
SELECT AlbumId AS Id FROM albums WHERE /*@albumId*/AlbumId = @albumId
```

A custom condition key is independent from query variables.

```sql
SELECT AlbumId AS Id FROM albums WHERE /*Current*/IsArchived = 0
```

[Markers](../conditional-sql/markers.md)

### RINKU1006 unbalanced scope

```sql
SELECT AlbumId) FROM albums
SELECT CASE WHEN IsArchived = 1 THEN 0 ELSE 1 END END FROM albums
```

A closing parenthesis or `END` has no matching open scope.

[Template syntax](../conditional-sql/template-syntax.md)

### RINKU1007 scope too deep

```text
64 nested parentheses, CASE blocks, or BEGIN blocks
RINKU1007
```

The template parser supports 63 nested scopes.

### RINKU1008 projection only construct

```sql
SELECT AlbumId AS Id!, Title FROM albums
```

The same marker is valid in a dynamic projection.

```sql
?SELECT AlbumId AS Id!, Title FROM albums
```

[Dynamic projection](../conditional-sql/dynamic-projection.md)

## Binding errors

### RINKU2001 no connection

```text
command.Connection == null
parser.Query(command)
RINKU2001
```

A command executed through Rinku needs a connection.

```csharp
using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id FROM albums";
Album album = parser.Query(command);
```

[Existing DbCommand](../running-queries/dbcommand.md)

### RINKU2002 required handler value

```csharp
static readonly QueryCommand ById = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_X)");
List<Album> albums = ById.Query<List<Album>>(cnn);
// RINKU2002
```

A conditional handler can disappear with an absent value.

```csharp
static readonly QueryCommand ById = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (?@albumIds_X)");
List<Album> albums = ById.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```

[Collection expansion](../conditional-sql/collections.md)

### RINKU2003 handler value type

```csharp
values.Use("@skip", "not a number");
// RINKU2003 when the template uses @skip_N.
```

```csharp
values.Use("@skip", 46);
values.Use("@skip", "46");
```

The handler decides which value types it accepts.

[Value handlers](../conditional-sql/handlers.md)

### RINKU2004 invalid parameter at index

```text
command.Parameters[index]
object is not IDbDataParameter
RINKU2004
```

This can occur with an application supplied `IDbCommand` implementation whose parameter collection returns an invalid item.

[Existing DbCommand](../running-queries/dbcommand.md)

### RINKU2005 value not set

```csharp
MultiVariableHandler handler = MultiVariableHandler.Build("@albumIds");
object? state = null;
handler.Update(command, ref state, new[] { 2, 5 });
// RINKU2005
```

`Update` expects state created by `SaveUse`. Builders perform that sequence for query execution.

[Custom conditional SQL](../customization/conditional-sql.md)

### RINKU2006 type carries no size

```csharp
SizedDbParamCache.Get(DbType.Int32, 100);
// RINKU2006
```

Size metadata applies to database types that carry a size.

```csharp
SizedDbParamCache.Get(DbType.String, 100);
```

[Parameter metadata](../running-queries/parameter-metadata.md)

## Mapping errors

### RINKU3001 no parser for the schema

The returned columns must satisfy one construction path for the requested type.

```sql
SELECT AlbumId, AlbumTitle FROM albums
```

```csharp
public record Album(int Id, string Title);
// AlbumId and AlbumTitle do not match Id and Title.
```

The SQL can adapt the names.

```sql
SELECT AlbumId AS Id, AlbumTitle AS Title FROM albums
```

The type can adapt the names instead.

```csharp
public record Album([Alt("AlbumId")] int Id, [Alt("AlbumTitle")] string Title);
```

A nested type also needs a mapping registration when it is reached through another type.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);
```

[Objects](../mapping/objects.md) · [Names](../mapping/names.md) · [Registration](../mapping/registration.md)

### RINKU3002 missing group boundary

```csharp
public record Report(List<int> Rows, int Total);
static readonly QueryCommand GetReport = new("SELECT RowId AS Rows, COUNT(*) OVER () AS Total FROM report_rows ORDER BY RowId");
Report report = GetReport.Query<Report>(cnn);
// RINKU3002 when no usable parent boundary can be negotiated.
```

A multi-row mapping needs a boundary for one complete parent value.

[Grouping](../mapping/grouping.md)

### RINKU3003 group key matched no column

```csharp
[GroupKeyColumns("AccountId")]
public record AccountSummary(string Name, List<int> InvoiceIds);
```

```sql
SELECT Name, InvoiceId AS InvoiceIds FROM invoices
-- AccountId is missing, RINKU3003.
```

The required grouping key must match the returned schema.

[Grouping](../mapping/grouping.md)

### RINKU3004 conflicting grouping rules

```csharp
public sealed class Batch
{
    [GroupKeyMethod(nameof(ByWindow))]
    public Batch([GroupKey] int id, List<string> items) { }

    public static (bool Same, int Next) ByWindow(int stored, int current) => default;
}
```

The same construction declares two grouping rule families.

[Grouping](../mapping/grouping.md)

## Reading errors

### RINKU4001 no results

```csharp
static readonly QueryCommand FindAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = FindAlbum.Query<Album>(cnn, new { albumId = 999 });
// RINKU4001 when no complete Album is returned.
```

A result shape can represent absence.

```csharp
Optional<Album> album = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });
List<Album> albums = FindAlbum.Query<List<Album>>(cnn, new { albumId = 999 });
```

[Result shapes](../running-queries/result-shapes.md)

### RINKU4002 result shape refused the results

```csharp
Album album = GetAlbum.Query<Single<Album>>(cnn, new { albumId = 12 });
// RINKU4002 when a second complete Album exists.
```

[Single result shapes](../running-queries/result-shapes.md)

### RINKU4003 database NULL not allowed

```csharp
public record Album(int Id, int ReleaseYear);
// NULL ReleaseYear produces RINKU4003.
```

```csharp
public record Album(int Id, int? ReleaseYear);
```

[Database NULL](../mapping/nulls.md)

### RINKU4004 cannot convert

```csharp
static readonly QueryCommand GetTitle = new("SELECT Title FROM albums WHERE AlbumId = @albumId");
int title = GetTitle.ExecuteScalar<int>(cnn, new { albumId = 12 });
// RINKU4004 when Title cannot be converted to int.
```

```csharp
string title = GetTitle.ExecuteScalar<string>(cnn, new { albumId = 12 });
```

[Construction and conversion](../mapping/construction-paths.md)

### RINKU4005 cannot read a dynamic column

```csharp
Version invalidId = row.Get<Version>("Id");
// RINKU4005 for an integer Id column.

int id = row.Get<int>("Id");
```

[Dynamic rows](../mapping/dynamic-rows.md)

## Configuration errors

### RINKU5001 type not usable by this parsing info

A custom `TypeParsingInfo` can reject a target type it does not support.

[Type registration](../customization/type-registration.md)

### RINKU5002 construction shape not usable

```csharp
static class BoxFactory
{
    public static Box<T> Create<T>(T value) => new(value);
}
```

An offered constructor or factory must have a construction shape that the target mapping can use.

[Construction paths](../mapping/construction-paths.md)

### RINKU5003 unusable member

```csharp
public class Row
{
    public int Id { get; }
}

PropertyInfo id = typeof(Row).GetProperty(nameof(Row.Id)) ?? throw new InvalidOperationException();
TypeParsingInfo.GetOrAdd<Row>().AddMember(id);
// RINKU5003 because Id has no setter.
```

[Mapping slot rules](../customization/slot-rules.md)

### RINKU5004 target type mismatch

```csharp
TypeParsingInfo info = TypeParsingInfo.GetOrAdd<Album>();
ConstructorInfo paymentConstructor = typeof(Payment).GetConstructors()[0];
info.AddPossibleConstruction(paymentConstructor);
// RINKU5004
```

The registered construction builds a different target type.

[Construction paths](../mapping/construction-paths.md)

### RINKU5005 construction from a foreign generic type

An open generic construction cannot be taken from an unrelated generic host.

A non generic host can expose a generic factory.

```csharp
static class BoxFactory
{
    public static Box<T> Create<T>(T value) => new(value);
}
```

[Construction paths](../mapping/construction-paths.md)

### RINKU5006 attribute on the wrong member type

```csharp
public class Options
{
    [ForBoolCond]
    public int IncludeDeleted { get; init; }
}
// RINKU5006
```

```csharp
public class Options
{
    [ForBoolCond]
    public bool IncludeDeleted { get; init; }
}
```

[Parameter members](../customization/parameter-members.md)

### RINKU5007 operation unsupported for this type

```csharp
JsonSerializer.Deserialize<DynaObject>("{}", options);
// RINKU5007
```

`DynaObject` gets its shape from a result schema and can be serialized after it has that shape.

[Dynamic rows](../mapping/dynamic-rows.md)

## Tracking errors

### RINKU6001 no copy strategy

The runtime tracking materializer could not build the requested tracked shape.

[Runtime tracking](../tracking/runtime.md)

### RINKU6002 copy method not usable

A configured tracking source method or property does not match the tracked member value type.

[Tracking items](../tracking/items.md) · [Runtime tracking](../tracking/runtime.md)

### RINKU6003 no current value

```text
tracked slot has no current value
slot is read for display
RINKU6003
```

[Tracking items](../tracking/items.md)

### RINKU6004 no factory for a new item

```csharp
IBindingList binding = tracked;
binding.AddNew();
// RINKU6004 when no new item factory or AddingNew handler provides an item.
```

[Tracking binding](../tracking/binding.md)

## Internal errors

### RINKU9001 internal invariant

`RINKU9001` reports an internal Rinku invariant failure.

```text
RINKU9001
include the stack trace, query template, target type, and result schema when available
```

[GitHub issues](https://github.com/RinkuLib/RinkuLib/issues)
