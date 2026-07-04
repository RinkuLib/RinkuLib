# Registration

Every type parses through its parsing info, the entry a registry keeps per type. This page covers how a type gets its entry and what registering can decide. What the entry holds is on [construction paths](construction-paths.md), and the slot-level rules live with their concepts, [nullability](nullability.md), [names](names.md), [reading order](reading-order.md).

## How a type gets its info

Querying a type registers it, whatever the `T`:

```csharp
Album album = GetAlbum.Query<Album>(cnn);   // Album registers on first use
```

Basic types and enums, anything a `DbDataReader` exposes directly, work on contact. Any other type must be known before the engine will consider it inside another one: a custom type reached only as a slot, with no registration, makes its construction path unsatisfiable. Three ways to make it known.

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

`GetOrAdd` saves a generic type under its definition by default, so one entry (`Result<>`) covers every closed form. Pass `saveAsGenericDefinitionWhenGeneric: false` to register a single closed form (`Result<int>`) with its own entry instead. Resolution takes the exact closed entry when one exists and falls back to the definition otherwise, so both can coexist: configure the definition for the general case, a closed form for the exception.

Constraints on generic entry points:

- A generic method's type arguments must match the target's exactly, in order and count.
- The declaring type of a factory cannot itself be generic.

```csharp
public class DataWrapper<T> { public DataWrapper(T value) { } }

public static class WrapperFactory {                    // valid, non-generic host
    public static DataWrapper<T> Create<T>(T value) => new(value);
}
public static class GenericFactory<T> {                 // invalid, generic host
    public static DataWrapper<T> Create(T value) => new(value);
}
```

## Registering with another info

Registration also decides which info the type gets, and that is the deepest lever: a different info parses by different rules. The built-in alternative is `CtorTypeInfo`, which negotiates by position and type alone, names ignored, [reading order](reading-order.md) sequential, `[DbConstructor]` picking the constructor when there are several. The `ValueTuple` definitions come registered with it, which is where [tuple mapping](../running-queries/result-shapes.md#tuples) comes from, and `DynaObject` is parsed by an info of its own too.

```csharp
TypeParsingInfo.GetOrAdd<Coordinates>(CtorTypeInfo.Instance);   // positional, names ignored
```

An info does not have to support every operation. The slot-level helpers, `SetInvalidOnNull`, `SetNullColHandler`, `UpdateAltName`, `UpdateNullColHandler`, take two roads: the info's own capability when it has one, or any info that can provide its slots, and they return `false` instead of throwing only when neither is available. The paths themselves are exposed by the default info, which is why the examples that walk them match on `DefaultTypeParsingInfo` with `is` rather than cast.
