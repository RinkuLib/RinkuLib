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

A column matches a slot when the name matches (case-insensitive) and the type is convertible. Among the viable paths, the engine takes the first one the columns fully satisfy. Paths are kept most-specific first, so that first match is usually the right one. The exact ordering is on [registration](registration.md).

## Alternative names

`[Alt]` adds an accepted name to a slot.

```csharp
public record Person(int Id, [Alt("Name")] string Username);
// matches a "Username" column or a "Name" column
```

For a column whose name is not a valid C# identifier, the engine also honors a `[TrueName("...")]` on the slot. There is no such attribute in RinkuLib: it is matched by name, so any attribute called `TrueNameAttribute` with a string value works. That is what lets [generated result records](../codegen/index.md) carry the real column name without depending on Rinku.

```csharp
public record Row([TrueName("Track Name")] string TrackName);
// matches the literal column "Track Name"
```

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

The same negotiation runs at every depth, with no special handling for a type that references itself. Each level consumes its own columns, and the descent goes exactly as deep as the columns reach.

```csharp
public record User(int Id, string Name, [Alt("Boss")] User? Supervisor = null);

// Columns: Id | Name | SupervisorId | SupervisorName | SupervisorBossId | SupervisorBossName
User u = GetUser.Query<User>(cnn, new { id = 3 });
// two levels of Supervisor columns, so u.Supervisor.Supervisor is filled,
// and its own Supervisor finds no columns and stays null
```

What stops a self-reference from looping is column consumption, not the prefix: a level that consumes no new column will not re-enter the same type. So the descent halts on its own once the columns run out. The nullable `Supervisor` is what lets the deepest level simply be left empty rather than fail. The prefix (`Supervisor`, `SupervisorBoss`) only routes distinct columns to each level, it is not itself the stopping rule.

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

If a constructor parameter and a settable member both map to the same column, the member assignment runs after the constructor and wins. Make the member non-public or non-writable if that is not what you want.
