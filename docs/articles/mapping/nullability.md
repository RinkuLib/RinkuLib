# Nullability

Every slot has a rule for what a `NULL` column does. The default follows the runtime type. Two attributes cover the common adjustments.

## The default

Inside an object being built, a slot follows its runtime type:

- A reference type accepts `NULL` and receives `null`.
- `Nullable<T>` accepts `NULL` and receives `null`.
- A non-nullable struct rejects `NULL` and throws (`NullValueAssignmentException`).

```csharp
public record Track(int Id, string Name, string? Composer);
// Id       -> NULL throws, an int cannot be null
// Name     -> NULL gives null
// Composer -> NULL gives null
```

The engine reads the runtime type, not the nullable-reference-type annotation. `string` and `string?` are the same to it, both accept `NULL`, so the `?` on a reference type carries no meaning here. Only value types split: a plain struct rejects `NULL`, `Nullable<T>` accepts it.

The type you query for directly is stricter: at the root only `Nullable<T>` accepts `NULL` on its own. `Query<int?>` gives `null`, `Query<string>` throws. Accepting null at the root of a reference type is a [result shape](../running-queries/result-shapes.md), `MaybeNull<T>`.

## `[NotNull]`, fail loudly

The runtime type of a reference slot cannot say "never null", which is why it defaults to accepting `NULL`. When your `string Name` really means it, say it with `[NotNull]` (the standard `System.Diagnostics.CodeAnalysis` attribute): `NULL` then throws instead of handing you `null`.

```csharp
public record Track(int Id, [NotNull] string Name);
// a NULL Name is a bug, fail loudly
```

The mirror exists too: `[MaybeNull]` makes a slot accept `NULL` regardless of its type.

The runtime form, for a type you cannot annotate, sets the rule by slot name:

```csharp
TypeParsingInfo.GetOrAdd<Track>().SetNullColHandler("Name", NotNullHandle.Instance);   // what [NotNull] declares
```

When the target is not just a name, the visitor form receives each slot and decides:

```csharp
TypeParsingInfo.GetOrAdd<Track>().UpdateNullColHandler(slot =>
    slot.Type == typeof(string)
        ? NotNullHandle.Instance   // every string slot rejects NULL
        : null);                   // null leaves the slot as is
```

## `[InvalidOnNull]`, collapse the object

`[InvalidOnNull]` on a slot says: if this value is `NULL`, do not build the object at all. The typical case is an optional joined object, where a missing match leaves its columns all `NULL`.

```csharp
public record struct Package([InvalidOnNull] int TrackingId, double Weight) : IDbReadable;

public record Shipment(int Id, Package? Contents);
// TrackingId NULL -> Contents is null, not a Package full of zeroes
```

The runtime form works by slot name:

```csharp
TypeParsingInfo.GetOrAdd<Package>().SetInvalidOnNull("TrackingId", true);
```

A collapse abandons the current object and lands on the slot that holds it. What happens there depends on that slot:

- A nullable slot (a reference type or `Nullable<T>`) receives `null`.
- A non-nullable slot throws, the same `NullValueAssignmentException` a plain `NULL` gives it. Mark that slot `[InvalidOnNull]` too and it collapses in turn, carrying the abandonment up to its own parent.
- At the top level, the collapse is a null result. A null-accepting shape takes it as empty, `MaybeNull<T>` or `OptionalNullable<T>` for a reference type, `T?` for a struct; plain `T`, `Optional<T>`, and `OptionalStruct<T>` throw. These are the same shapes that accept a `NULL` value, see [result shapes](../running-queries/result-shapes.md).

```csharp
public record struct Bottom([InvalidOnNull] int Key, string Name) : IDbReadable;
public record Middle(int Id, [InvalidOnNull] Bottom Bottom) : IDbReadable;
public record Top(int Id, Middle? Middle);
// Bottom.Key is NULL -> Bottom collapses -> Middle's [InvalidOnNull] Bottom collapses Middle -> Top.Middle (nullable) is null
```

Note the difference with [result shapes](../running-queries/result-shapes.md): zero rows is a query-level outcome, a `NULL` column is a slot-level one.
