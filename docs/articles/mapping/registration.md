# Registration

Every type parses through its parsing info, the entry a registry keeps per type. This page covers how a type gets its entry and what registering can decide. What the entry holds is on [construction paths](construction-paths.md), and the slot-level rules live with their concepts, [nullability](nullability.md), [names](names.md), [reading order](reading-order.md).

## How a type gets its info

Querying a type registers it, whatever the `T`:

```csharp
Album album = GetAlbum.Query<Album>(cnn);   // Album registers on first use
```

Basic types and enums, anything a `DbDataReader` exposes directly, work on contact. Any other type must be known before the engine will consider it inside another one. A custom type reached only as a slot, with no registration, makes its construction path unsatisfiable. There are three ways to make it known.

The `IDbReadable` marker, registering the type wherever it appears:

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);   // Artist resolves as a nested slot
```

`[AreReadable]` on a constructor or factory, registering all of its parameter types, one entry point for a whole graph:

```csharp
[method: AreReadable]
public record Invoice(int Id, Customer Customer, Address Shipping);
// Customer and Address register along with Invoice
```

And manually:

```csharp
var info = TypeParsingInfo.GetOrAdd<Address>();
```

Separate from the rules, a type's implementation may register more on its own: `List<TInner>` and `Optional<TInner>` register their element, which is why querying `List<Track>` makes `Track` usable too.

## Generic types

`GetOrAdd` saves a generic type under its definition by default, so one entry (`Result<>`) covers every closed form. Pass `saveAsGenericDefinitionWhenGeneric: false` to register a single closed form (`Result<int>`) with its own entry instead. Resolution takes the exact closed entry when one exists and falls back to the definition otherwise, so both can coexist. Configure the definition for the general case, a closed form for the exception.

```csharp
var forAll  = TypeParsingInfo.GetOrAdd(typeof(Result<>));                          // every Result<T>
var forInts = TypeParsingInfo.GetOrAdd<Result<int>>(saveAsGenericDefinitionWhenGeneric: false); // just Result<int>
```

Registering a generic *method* as a construction path for such a type is on [construction paths](construction-paths.md#generic-factories).

## Registering with another info

Registration also decides *which info* parses the type, the deepest lever there is. A different info negotiates by different rules. The built-in `CtorTypeInfo` maps by position and type alone, names ignored, [reading order](reading-order.md) sequential.

```csharp
public record struct Coordinates(double Lat, double Long);

TypeParsingInfo.GetOrAdd<Coordinates>(CtorTypeInfo.Instance);
// Columns: Longitude | Latitude  ->  Lat takes the first double, Long the second, names disregarded
```

With several constructors, `[DbConstructor]` marks the one to use. Without it, the first constructor that takes parameters wins.

```csharp
public class Segment {
    public Segment(int start) { }
    [DbConstructor] public Segment(int start, int end) { }   // CtorTypeInfo builds through this one
}
```

The name-ignoring built-ins are wired the same way: the `ValueTuple` definitions ship registered with `CtorTypeInfo`, the source of [tuple mapping](../running-queries/result-shapes.md#tuples), and [`DynaObject`](dynaobject.md) runs on an info of its own. These are built-ins, but an info is just a `TypeParsingInfo`. You can write your own and register it, and it parses the type however you code it to.

### What an info supports

Every customization goes through a capability interface, and an info implements the ones it chooses to support. The default info implements all of them. `CtorTypeInfo` implements none, so every helper declines on it:

```csharp
var info = TypeParsingInfo.GetOrAdd<Coordinates>(CtorTypeInfo.Instance);

bool a = info.UpdateNullColHandler("Lat", NotNullHandle.Instance);               // false: CtorTypeInfo does not implement it
bool b = info.AddPossibleConstruction(typeof(Coordinates).GetConstructors()[0]); // false: same
```

When an info does not implement a helper's interface, the helper returns `false` instead of throwing. So you match on the interface, never on a concrete type. Your own info can implement any of these interfaces, and the same helpers work on it just as they do on the default.
