# ValueTuple mapping

*Name-agnostic, positional mapping.*

The engine can map directly to a `ValueTuple`. At the **first level it ignores the element names** (`Item1`, `Item2`). For complex elements this means it does not look for a prefix. A property `Name` is matched directly against `Name`, not `UserName`.

## Basic, mixed, complex

```csharp
// Basic, strictly sequential by column order
var (id, name) = await cmd.QueryAsync<(int, string)>(cnn, ct: ct);

// Mixed, the basic type consumes the next column, then the complex type negotiates
var (id, employee) = await cmd.QueryAsync<(int, Location)>(cnn, ct: ct);

// Complex, each object matches its own member names directly
public record struct Person(int ID, string Name);
var (p1, p2) = await cmd.QueryAsync<(Person, Person)>(cnn, ct: ct);
```

## Pitfalls

Without a name fence (a prefix), some shapes scavenge unexpectedly.

**Unexpected consumption**, schema `| ID | Name | ID | Name | Email |`.

```csharp
public struct Person { int ID; string Name; string Email; }
// Person 1 (parameterless ctor) scavenges ID(0), Name(1) AND Email(4).
// Person 2 gets ID(2), Name(3) but no Email, already consumed.
var (p1, p2) = await cmd.QueryAsync<(Person, Person)>(cnn, ct: ct);
```

**Unexpected sequence**, schema `| ID | Name | RoleId | Email |`.

```csharp
// Person maps ID(0), Name(1) and Email(3), so the cursor jumps to index 3.
// The following int then looks at index 4 and fails to match.
var (person, roleId) = await cmd.QueryAsync<(Person, int)>(cnn, ct: ct);
```

## Controlling column scavenging

Three parameter/member attributes tune which columns a slot may match, which is how you resolve the pitfalls above.

| Attribute | Effect |
| --- | --- |
| `[CanNotLookAnywhere]` | Force sequential matching, only the column right after the last used one. Stops a member from scavenging a far-away column (fixes "unexpected consumption"). |
| `[CanLookAnywhere]` | Allow matching any column, not just the next (fixes "unexpected sequence"). |
| `[MayReuseCol]` | Allow matching a column another slot already consumed. |

```csharp
public struct Person {
    public int ID;
    public string Name;
    [CanNotLookAnywhere] public string Email;   // won't jump ahead to a later Email column
}
```

These are `IUsageFlagModifier` attributes (see [custom parsing & matching](custom-parsing.md)). The default sits between the two extremes, so reach for them only when a tuple or nested shape matches the wrong column.
