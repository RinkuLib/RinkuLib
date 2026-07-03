# Value tuples

A `ValueTuple` maps positionally. Element names (`Item1`, or the names in `(int Id, string Name)`) are ignored, and complex elements match their own member names with no prefix.

```csharp
// Basic: strictly by column order
var (id, name) = cmd.Query<(int, string)>(cnn);

// Mixed: the basic element takes the next column, the complex one negotiates its own
var (id, location) = cmd.Query<(int, Location)>(cnn);

// Complex: each element matches its member names directly
public record struct Person(int Id, string Name);
var (p1, p2) = cmd.Query<(Person, Person)>(cnn);
// Columns: Id | Name | Id | Name  -> p1 takes the first pair, p2 the second
```

Duplicate column names work: each element consumes columns left to right, so repeated `Id | Name` pairs land in order.

## Pitfalls

Without a name prefix, a complex element can match further than you expect.

**Unexpected consumption.** Columns: `Id | Name | Id | Name | Email`.

```csharp
public struct Person { public int Id; public string Name; public string Email; }

var (p1, p2) = cmd.Query<(Person, Person)>(cnn);
// p1 takes Id(0), Name(1) and reaches ahead for Email(4).
// p2 gets Id(2), Name(3) and no Email, it was consumed.
```

**Unexpected sequence.** Columns: `Id | Name | RoleId | Email`.

```csharp
var (person, roleId) = cmd.Query<(Person, int)>(cnn);
// Person takes Id(0), Name(1), Email(3), moving the cursor past RoleId.
// The int then has nothing left to match.
```

## Controlling how far a slot may look

Three attributes tune which columns a slot may take. They fix the pitfalls above.

| Attribute | Effect |
| --- | --- |
| `[CanNotLookAnywhere]` | Only the column right after the last used one. |
| `[CanLookAnywhere]` | Any column, not just the next region. |
| `[MayReuseCol]` | A column another slot already consumed. |

```csharp
public struct Person {
    public int Id;
    public string Name;
    [CanNotLookAnywhere] public string Email;   // stays put, no reaching ahead
}
```

The default sits between the extremes. Reach for these only when a tuple or nested shape grabs the wrong column.
