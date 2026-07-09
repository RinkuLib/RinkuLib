# Objects and nesting

The engine builds an object by finding a construction path the columns can satisfy, a constructor or a static factory, optionally followed by filling settable members.

## What it can use

With no annotations:

- Public constructors, parameters matched by name and type against the columns.
- Public static factory methods on the type that return the type itself.
- Public settable fields and properties, filled after construction (when [below](#post-construction-members)). `init`-only properties are not settable this way, they can only run during construction, so make them constructor parameters instead.

```csharp
public record Track(int Id, string Name, decimal UnitPrice);   // constructor

public class Playlist {                                         // parameterless constructor
    public int Id { get; set; }
    public string? Name { get; set; }                           // both settable, filled after
}
```

A column matches a slot when the name matches (case-insensitive) and the type is convertible. Among the viable paths, the engine takes the first one the columns fully satisfy. Paths are kept most-specific first, so that first match is usually correct. The exact ordering is on [construction paths](construction-paths.md).

## Default values

A parameter with a default value is optional to the negotiation. When nothing satisfies it, no matching column for a simple type, no satisfiable construction for a complex one, the slot falls back to its default instead of failing the path. One constructor with defaults covers what several arities would. `(int, string, string?)` below also stands in for `(int, string)`.

```csharp
public record Track(int Id, string Name, string? Composer = null);
// Columns: Id | Name            -> builds, Composer stays null
// Columns: Id | Name | Composer -> builds with all three
```

The fallback emits the type's default, so it applies when the declared default is exactly that (`= null`, `= 0`, `= false`). A parameter with `= 5` is not optional, the engine will not fabricate the value. Post-construction members need none of this, they are optional by nature and stay unset when nothing matches.

The runtime form is an `IFallbackParserGetter` on the slot, carried by a `ParamInfoPlus` on the [construction path](construction-paths.md#replacing-the-set). `DefaultValueFallback.Instance` is the type-default fallback a compiler default produces, so attaching it makes a slot optional even when its parameter declares nothing:

```csharp
public record Track(int Id, string Name, int Code);   // Code is required, no = default

if (TypeParsingInfo.GetOrAdd<Track>() is ICanProvideConstructions info) {
    var slots = info.PossibleConstructors[0].Parameters;
    var s = slots[2];                                  // Code
    slots[2] = new ParamInfoPlus(s.Type, s.NullColHandler, s.NameComparer,
        IColModifier.Nothing, DefaultValueFallback.Instance);
    // a schema without a Code column now builds, Code falling back to 0
}
```

Going runtime reaches past what a declared default can say. A default only fires when it equals the type default, so `= 5` is ignored. Your own `IFallbackParserGetter` returns any parser for the missing slot, so it can supply a non-default constant, a computed value, or a whole alternative construction.

## Alternative names

`[Alt]` adds an accepted name to a slot.

```csharp
public record Person(int Id, [Alt("Name")] string Username);
// matches a "Username" column or a "Name" column
```

Names go further, skipping prefix segments or matching by position alone. The full rules are on [names](names.md).

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

A level that consumes no new column stops instead of descending into the same type again, so the recursion ends once the columns run out. The `= null` [default](#default-values) is what lets it end cleanly. At the deepest level no `Supervisor` columns remain, so the slot falls back to its default and the path still builds. Without it, every level would require a `Supervisor` and the negotiation could never resolve.

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

After a construction path is chosen, the columns it did not consume can fill the type's public settable fields and properties. This completion runs automatically only for the parameterless constructor. To enable it after any other constructor or factory, mark that path `[CanCompleteWithMembers]`.

```csharp
[method: CanCompleteWithMembers]
public class Metadata(string value) {
    public string Value { get; set; } = value;   // settable, but the constructor already consumed "Value"
    public string? Source { get; set; }           // member: fills the leftover "Source" column
}
// Columns: Value | Source  ->  the constructor takes Value and completion leaves it untouched, Source fills from the rest
```

Members negotiate over the columns the constructor left behind, by the same name and type matching used everywhere. A settable member is not an override. One whose name matches a consumed column finds it already taken, looks among the remaining columns, and is left as construction set it when none fit. To let a member read a column a parameter already used, mark it `[MayReuseCol]`, one of the [reading-order](reading-order.md) flags.
