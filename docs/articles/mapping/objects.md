# Objects and nesting

The engine builds an object by finding a construction path the columns can satisfy: a constructor or a static factory, optionally followed by filling settable members.

## What it can use

With no annotations:

- Public constructors, parameters matched by name and type against the columns.
- Public static factory methods on the type that return the type itself.
- Public settable fields and properties, filled after construction (when [below](#post-construction-members)). `init`-only properties are not settable this way, they can only run during construction, so make them constructor parameters instead.

```csharp
public record Track(int Id, string Name, decimal UnitPrice);   // constructor

public class Playlist {
    public Playlist(int id) { Id = id; }                        // constructor fills Id
    public int Id { get; }
    public string? Name { get; set; }                           // settable, filled after
}
```

A column matches a slot when the name matches (case-insensitive) and the type is convertible. Among the viable paths, the engine takes the first one the columns fully satisfy. Paths are kept most-specific first, so that first match is usually the right one. The exact ordering is on [construction paths](construction-paths.md).

## Default values

A parameter with a default value is optional to the negotiation. When nothing satisfies it, no matching column for a simple type, no satisfiable construction for a complex one, the slot falls back to its default instead of failing the path. One constructor with defaults covers what several arities would: `(int, string, string?)` below also stands in for `(int, string)`.

```csharp
public record Track(int Id, string Name, string? Composer = null);
// Columns: Id | Name            -> builds, Composer stays null
// Columns: Id | Name | Composer -> builds with all three
```

The fallback emits the type's default, so it applies when the declared default is exactly that (`= null`, `= 0`, `= false`). A parameter with `= 5` is not optional, the engine will not fabricate the value. Post-construction members need none of this, they are optional by nature and simply stay unset when nothing matches.

## Alternative names

`[Alt]` adds an accepted name to a slot.

```csharp
public record Person(int Id, [Alt("Name")] string Username);
// matches a "Username" column or a "Name" column
```

Names go further, skipping prefix segments, matching by position alone: the full rules are on [names](names.md).

## Nesting

A slot whose type is complex recurses. Its columns are matched with a name prefix built from the path.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

// Columns: Id | Title | ArtistId | ArtistName
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

`Artist.Id` matches `ArtistId`, `Artist.Name` matches `ArtistName`. The prefix grows with depth, which keeps two nested objects of the same shape from taking each other's columns.

Nested custom types must be registered. `IDbReadable` on the nested type is the usual way (see [registration](registration.md)).

## Recursion

The same negotiation runs at every depth, a self-referencing type included. Each level consumes its own columns, and the descent goes exactly as deep as the columns reach.

```csharp
public record User(int Id, string Name, [Alt("Boss")] User? Supervisor = null);

// Columns: Id | Name | SupervisorId | SupervisorName | SupervisorBossId | SupervisorBossName
User u = GetUser.Query<User>(cnn, new { id = 3 });
// two levels of Supervisor columns, so u.Supervisor.Supervisor is filled,
// and its own Supervisor finds no columns and stays null
```

A level that consumes no new column stops instead of descending into the same type again, so the recursion ends once the columns run out. The `= null` [default](#default-values) is what lets it end cleanly: at the deepest level no `Supervisor` columns remain, so the slot falls back to its default and the path still builds. Without it, every level would require a `Supervisor` and the negotiation could never resolve.

## Factories and polymorphism

A factory can return a derived type, so an interface-typed slot can produce different implementations depending on which columns are present.

```csharp
public interface IPayment : IDbReadable {
    public static IPayment CreateCard(string cardNumber) => new Card(cardNumber);
    public static IPayment CreateTransfer(string iban, string bic) => new Transfer(iban, bic);
}
public record Card(string CardNumber) : IPayment;
public record Transfer(string Iban, string Bic) : IPayment;

public record Order(int Id, IPayment Payment);

// Columns: Id | PaymentIban | PaymentBic      -> Payment is a Transfer
// Columns: Id | PaymentCardNumber             -> Payment is a Card
```

The columns decide which factory is satisfiable, and the first fully-satisfied path builds the object.

## Post-construction members

After the constructor runs, remaining columns can fill public settable fields and properties. By default this happens only when the chosen path is the parameterless constructor. To allow it after any other constructor or factory, mark that path `[CanCompleteWithMembers]`.

```csharp
[method: CanCompleteWithMembers]
public class Metadata(string value) {
    public string Value { get; } = value;
    public string? Source { get; set; }   // filled from a "Source" column after construction
}
```

A member fills only a column the constructor did not already consume. If a member matches the same name as a constructor parameter, the parameter has already taken that column, so the member looks among the remaining columns instead, and stays at its default if it finds none. Mark the member `[MayReuseCol]` to let it read a column a parameter already used.
