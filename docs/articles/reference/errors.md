# Errors

Every failure RinkuLib raises carries a code. The code is on the exception, prefixed to the message, and
its `HelpLink` points at the entry below.

```csharp
try {
    var user = cmd.Query<User>(cnn);
}
catch (RinkuException e) {
    logger.LogError("{Code} {Message} {Help}", e.Code, e.Message, e.HelpLink);
}
```

Catch a family to handle a class of failure, or match the code for one condition.

```csharp
catch (RinkuReadException e) when (e.Code == ErrorCodes.NoRows) {
    return Array.Empty<User>();
}
```

## Families

| type | band | raised while |
| --- | --- | --- |
| `RinkuTemplateException` | RINKU1### | reading the template |
| `RinkuBindingException` | RINKU2### | preparing a command from it |
| `RinkuMappingException` | RINKU3### | building a parser for the target type |
| `RinkuReadException` | RINKU4### | reading a result through that parser |
| `RinkuConfigurationException` | RINKU5### | configuring a type |
| `RinkuTrackingException` | RINKU6### | copying or editing a tracked value |
| `RinkuInternalException` | RINKU9### | an invariant inside the library did not hold |

All of them derive from `RinkuException`.

## RINKU1001, query too short {#rinku1001}

The template is under two characters.

## RINKU1002, unclosed comment {#rinku1002}

```sql
SELECT /*IncludeEmail FROM Users
```

The marker opened at `/*` and no `*/` closes it, so the rest of the template was read as its key.

```sql
SELECT /*IncludeEmail*/Email, Name FROM Users
```

A literal comment closes the same way.

```sql
SELECT /*~ index hint */Name FROM Users
```

## RINKU1003, empty condition key {#rinku1003}

A marker holds a key with nothing in it.

```sql
SELECT /**/Col FROM Users
SELECT /*   */Col FROM Users
```

A marker chains keys with `&` and `|`, so a connector with nothing on one side of it leaves an empty key
the same way.

```sql
SELECT /*IsAdmin&*/Col FROM Users
SELECT /*&IsAdmin*/Col FROM Users
SELECT /*IsAdmin|*/Col FROM Users
SELECT /*|IsAdmin*/Col FROM Users
SELECT /*IsAdmin&&IsOwner*/Col FROM Users
```

Drop the stray connector, or supply the key that belongs on that side.

```sql
SELECT /*IsAdmin*/Col FROM Users
SELECT /*IsAdmin&IsOwner*/Col FROM Users
```

See [conditional markers](../conditional-sql/conditional-markers.md).

## RINKU1004, unknown handler suffix {#rinku1004}

```sql
SELECT * FROM tracks WHERE GenreId IN (@genreIds_Q)
```

No handler is registered for `_Q`. The message names the suffix and the variable it sat on. Here the
collection spread is `_X`.

```sql
SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)
```

The registered letters, and how to add one, are on [handlers](../conditional-sql/handlers.md).

## RINKU1005, condition variable not in the query {#rinku1005}

```sql
SELECT a FROM t WHERE /*@Nope*/x = 1
```

A marker naming a variable keys off that variable being supplied, so the variable has to be in the
template. `@Nope` is not, and the message names it.

```sql
SELECT a FROM t WHERE /*@Min*/x >= @Min
```

A marker that keys off nothing but its own name needs no variable.

```sql
SELECT a FROM t WHERE /*Recent*/x = 1
```

## RINKU1006, unbalanced scope {#rinku1006}

```sql
SELECT a) FROM Users
SELECT (a)) FROM Users
```

The `)` closes a parenthesis the template never opened.

`CASE` and `BEGIN` sit on the same counter, so a spare `END` reaches this too.

```sql
SELECT CASE WHEN a = 1 THEN 1 ELSE 0 END END FROM t
SELECT a END FROM t
```

## RINKU1007, scope too deep {#rinku1007}

Nesting reaches 63, and `CASE` and `BEGIN` open a scope that its `END` closes, drawing on the same budget
as `(`.

```sql
SELECT CASE WHEN a IN (SELECT x FROM (SELECT y FROM t) i) THEN 1 ELSE 0 END FROM u
--     ^1             ^2             ^3
```

A statement mixing them runs out at 63 between the two, so 31 `CASE` around 32 parentheses is the last
that parses.

## RINKU1008, projection-only construct {#rinku1008}

```sql
SELECT TrackId!, Name FROM tracks
```

`!` marks a column as always kept, which reads as such inside a dynamic projection. Make the projection
dynamic, or drop the `!`.

```sql
?SELECT TrackId!, Name FROM tracks
SELECT TrackId, Name FROM tracks
```

See [dynamic projection](../conditional-sql/dynamic-projection.md).

## RINKU2001, no connection {#rinku2001}

```csharp
var cmd = new SqliteCommand("SELECT Name FROM Users");
var name = parser.Query(cmd);   // RINKU2001
```

The command carries no connection to run against.

```csharp
var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT Name FROM Users";
var name = parser.Query(cmd);
```

## RINKU2002, required handler value {#rinku2002}

```csharp
static readonly QueryCommand ByGenre = new("SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)");

var tracks = ByGenre.Query<List<Track>>(cnn);   // RINKU2002, nothing supplied for @genreIds
```

`_X` builds the SQL out of the collection it is handed, so it has nothing to write without one. The
refusal comes while the SQL is being built, before any database call.

```csharp
var tracks = ByGenre.Query<List<Track>>(cnn, new { genreIds = new[] { 7, 8 } });
```

Marking the variable optional prunes its footprint instead of asking for a value.

```sql
SELECT * FROM tracks WHERE GenreId IN (?@genreIds_X)
```

```sql
-- nothing supplied
SELECT * FROM tracks
```

See [optional variables](../conditional-sql/optional-variables.md) and
[handlers](../conditional-sql/handlers.md).

## RINKU2003, handler value type {#rinku2003}

```csharp
b.Use("@ms", "abc");   // RINKU2003 on @ms_N
```

`_N` renders a number and `"abc"` does not convert to one. The message names the type it was handed.
Values that convert are taken.

```csharp
b.Use("@ms", 46);      // 46
b.Use("@ms", "46");    // 46
```

`_S` and `_R` take any value, see [handlers](../conditional-sql/handlers.md).

## RINKU2004, invalid parameter at index {#rinku2004}

The command learns how to bind by reading the parameters already on it, and one of them was not a
parameter. The providers reject a non-parameter as it is added, so this comes from an `IDbCommand` of your
own whose parameter collection accepts anything.

## RINKU2005, value not set {#rinku2005}

```csharp
var handler = MultiVariableHandler.Build("@genreIds");
object? state = null;
handler.Update(cmd, ref state, new[] { 7, 8 });   // RINKU2005
```

`Update` reads the state a bind wrote, and no bind ran. `SaveUse` writes it. The builders bind before they
update, so this comes from driving a `SpecialHandler` yourself.

## RINKU2006, type carries no size {#rinku2006}

```csharp
SizedDbParamCache.Get(DbType.Int32, 100);   // RINKU2006
```

A size belongs to the types that carry one, `String`, `AnsiString`, `Binary` and their kin. A fixed-width
type has nowhere to put it.

## RINKU3001, no parser for the schema {#rinku3001}

Every construction path for the target type was tried and none could be filled from the columns the query
returned. `RinkuNoParserException` carries `TargetType` and the `Schema` it was offered, so the message
shows both halves of the pair that could not be linked.

```
RINKU3001: cannot make the parser for Track with the schema (Int32 TrackId, String TrackName)
```

A query and a type each valid on their own still have to meet. Names, types, and registration are where
they miss most often, and an info or parser of your own brings its own conditions.

**The names.**

```sql
SELECT TrackId, TrackName FROM tracks
```

```csharp
public record Track(int Id, string Name);
```

`Id` finds no `Id` column. Alias in the SQL, or accept the other name on the slot.

```sql
SELECT TrackId AS Id, TrackName AS Name FROM tracks
```

```csharp
public record Track([Alt("TrackId")] int Id, [Alt("TrackName")] string Name);
```

A nested slot carries a prefix, see [names](../mapping/names.md).

**The types.**

```sql
SELECT Id, Token FROM sessions   -- Token is an integer column
```

```csharp
public record Session(int Id, Guid Token);
```

An integer does not reach a `Guid`, so that slot has no column it can take.

**The registration.**

```sql
SELECT Id, Title, ArtistId, ArtistName FROM albums JOIN artists ON ...
```

```csharp
public record Artist(int Id, string Name);                  // never registered
public record Album(int Id, string Title, Artist Artist);   // RINKU3001 names Album
```

A type reached only as a slot is considered once it is known, and `Artist` is not, so `Album` has no
satisfiable path. `IDbReadable` on `Artist` is the shortest fix.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
```

`[AreReadable]` on a constructor registers a whole graph, and `TypeParsingInfo.GetOrAdd<Artist>()` covers a
type you cannot annotate, see [registration](../mapping/registration.md).

When the columns are right and a slot has none, [objects](../mapping/objects.md#default-values) covers
defaults and [construction paths](../mapping/construction-paths.md) covers adding a path.

## RINKU4001, no rows {#rinku4001}

```csharp
string name = cmd.Query<string>(cnn);   // RINKU4001 when the query returns nothing
```

A plain type has no way to say the row was missing. The shapes that hold absence do.

```csharp
Optional<string> name = cmd.Query<Optional<string>>(cnn);   // null
List<string> names = cmd.Query<List<string>>(cnn);          // empty
```

See [result shapes](../running-queries/result-shapes.md).

## RINKU4002, a shape refused the result {#rinku4002}

```csharp
var only = cmd.Query<Single<User>>(cnn);   // RINKU4002 when the query returns two
```

A result shape's own parser turned down the rows it was handed, and the message says which rule was
broken. `Single<T>` asserts exactly one row.

```csharp
var first = cmd.Query<User>(cnn);       // takes the first, ignores the rest
var all = cmd.Query<List<User>>(cnn);   // takes them all
```

A shape is a registered type with a parser, and `RinkuShapeException` is public, so a parser can carry this
code. See [result shapes](../running-queries/result-shapes.md) and [parsers](../mapping/parsers.md).

## RINKU4003, null not allowed {#rinku4003}

```csharp
public record Track(int Id, string Name, double Weight);
// a NULL Name   -> null, the slot takes it
// a NULL Weight -> RINKU4003
```

The rule follows the runtime type, not the annotation, so a plain struct is what rejects `NULL` inside an
object. `string` and `string?` read the same. `NullValueAssignmentException` names the slot, its type, and
its parent.

```csharp
public record Track(int Id, string Name, double? Weight);   // accepts it, receives null
```

`[NotNull]` is what makes a reference slot reject `NULL`. At the root only `Nullable<T>` accepts it, so
`Query<string>` raises this and `Query<MaybeNull<string>>` takes it.

The attributes that change a slot's rule, including collapsing an object whose join found no match, are on
[nullability](../mapping/nullability.md).

## RINKU4004, cannot convert {#rinku4004}

Raised where a value is converted at run time rather than read into a shape the negotiation already
checked, which is what `ExecuteScalar<T>` does with whatever the query returns.

```csharp
static readonly QueryCommand FirstName = new("SELECT Name FROM Users WHERE ID = @id");

int name = FirstName.ExecuteScalar<int>(cnn, new { id = 1 });
// RINKU4004: Unable to parse from John (object : System.String) to System.Int32
```

The message carries the value, its runtime type, and the target type. Ask for the type the query returns,
or return a value that converts.

```csharp
string name = FirstName.ExecuteScalar<string>(cnn, new { id = 1 });
```

A column read into a mapped type takes the other road, where a slot the columns cannot feed is RINKU3001
instead.

## RINKU4005, cannot read a column {#rinku4005}

```csharp
row.Get<Version>("Id");   // the column holds an int
row.Get<int>("Id");
```

The message names the column, by whichever of index or name it was addressed with. See
[DynaObject](../mapping/dynaobject.md).

## RINKU5001, type not usable by this info {#rinku5001}

The parsing info was asked to handle a type it cannot, and every info decides that for itself. Among the
built-in ones, `BaseTypeInfo` takes base types and enums, `CtorTypeInfo` needs a constructor with
parameters, `DynaObjectTypeInfo` takes `DynaObject`, and a `DefaultTypeParsingInfo` takes the one type it
was built for. An info you write raises this from its own `ValidateCanUseType`. See
[registration](../mapping/registration.md).

## RINKU5002, construction shape not usable {#rinku5002}

```csharp
static class BoxDonor<T> {
    public static Box<T> Create(T value) => new(value);   // RINKU5002
}
```

The factory takes `T` from the class it sits on, which is the shape for a factory on the type it builds,
not for one on an outside host. There the method carries the parameter.

```csharp
static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);
}
```

The message names which part of the shape does not line up, a factory that is not static and one whose
type parameters differ in count or order being the others. See
[generic factories](../mapping/construction-paths.md#generic-factories).

## RINKU5003, unusable member {#rinku5003}

```csharp
class Row {
    public int Id { get; }            // no setter
    public event Action? Changed;
}

TypeParsingInfo.GetOrAdd<Row>().AddMember(typeof(Row).GetProperty("Id")!);       // RINKU5003
TypeParsingInfo.GetOrAdd<Row>().AddMember(typeof(Row).GetEvent("Changed")!);     // RINKU5003
```

The engine fills a member by writing to it, so it takes a field, a settable property, or a setter method.
The message names which way the offered member falls short. It is the member counterpart of RINKU5002, and
a setter takes its type parameters from the instance it writes to the same way a factory takes them from
the type it builds. See
[post-construction members](../mapping/construction-paths.md#post-construction-members).

## RINKU5004, target type mismatch {#rinku5004}

```csharp
var info = TypeParsingInfo.GetOrAdd<Album>();
info.AddPossibleConstruction(typeof(Payment).GetConstructors()[0]);   // RINKU5004
```

The path builds a `Payment` and the info builds an `Album`, so it could never satisfy that info. A member
belonging to another type raises it the same way. The message names both types.

## RINKU5005, offered from a foreign generic type {#rinku5005}

```csharp
static class BoxDonor<TAnything> {
    public static Box<T> Create<T>(T value) => new(value);
}

TypeParsingInfo.GetOrAdd(typeof(Box<>))
    .AddPossibleConstruction(typeof(BoxDonor<int>).GetMethod("Create")!);   // RINKU5005
```

The engine needs a fixed host to call the factory on, which a generic host does not give it. Move it to a
non-generic one.

```csharp
static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);
}
```

A member added from a foreign generic type raises this the same way. See
[generic factories](../mapping/construction-paths.md#generic-factories).

## RINKU5006, attribute on the wrong member type {#rinku5006}

```csharp
class Args {
    [ForBoolCond] public int IsAdmin { get; set; }   // RINKU5006
}
```

`[ForBoolCond]` drives a bool. The message names the attribute and the type it requires.

```csharp
class Args {
    [ForBoolCond] public bool IsAdmin { get; set; }
}
```

## RINKU5007, operation not supported for this type {#rinku5007}

```csharp
JsonSerializer.Deserialize<DynaObject>("{}", options);   // RINKU5007
```

A `DynaObject` takes its shape from the columns a reader returned, so there is nothing to rebuild it from
on the way back in. Writing one to JSON works, reading one does not.

## RINKU6001, no copy strategy {#rinku6001}

Tracking keeps a copy of the original to compare against, so a tracked type has to be copyable. This is
raised when its shape gives the copier nothing to work with, an abstract type or interface reached
directly, a type with no usable constructor, or a multi-dimensional array. The message names the type.

See [copying](../tracking/copying.md).

## RINKU6002, copy method not usable {#rinku6002}

A type declared how to copy itself and that declaration cannot be called. `ICopyable<T>` without the
`Copy` method it implies, or a `[CopyUsingMethod]` naming a method that does not exist, takes parameters,
or returns something that is not the type.

```csharp
[CopyUsingMethod("Clone")]
public class Row {
    public Row Clone() => new();   // zero parameters, returns the type
}
```

See [copying](../tracking/copying.md).

## RINKU6003, no current value {#rinku6003}

A tracked slot was read for display and holds no current value.

## RINKU6004, no factory for a new item {#rinku6004}

```csharp
IBindingList list = tracked;
list.AddNew();   // RINKU6004
```

`AddNew` needs a way to make the item. Supply a factory, or handle the `AddingNew` event. See
[items and lists](../tracking/items-and-lists.md).

## RINKU9001, internal invariant {#rinku9001}

An invariant inside the library did not hold, which is a bug in RinkuLib rather than a mistake in the
calling code. The message names the invariant.

Please report it with the stack trace and, if you can, the query and the target type.
