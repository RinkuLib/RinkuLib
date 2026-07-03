# Nullability

Every slot has a rule for what a `NULL` column does. The default follows the C# type. Two attributes cover the common adjustments.

## The default

- A reference type or `Nullable<T>` accepts `NULL` and receives `null`.
- A non-nullable struct rejects `NULL` and throws (`NullValueAssignmentException`).

```csharp
public record Track(int Id, string Name, string? Composer);
// Id       -> NULL throws, an int cannot be null
// Name     -> NULL gives null
// Composer -> NULL gives null
```

You get what the type signature implies.

## `[NotNull]`, fail loudly

`[NotNull]` (the standard `System.Diagnostics.CodeAnalysis` attribute) on a nullable slot makes `NULL` throw instead of handing you `null`. For columns that are nullable in the schema but must never be null in practice.

```csharp
public record Track(int Id, [NotNull] string Name);
```

## `[InvalidOnNull]`, collapse the object

`[InvalidOnNull]` on a slot says: if this value is `NULL`, do not build the object at all. The typical case is an optional joined object, where a missing match leaves its columns all `NULL`.

```csharp
public record struct Package([InvalidOnNull] int TrackingId, double Weight) : IDbReadable;

public record Shipment(int Id, Package? Contents);
// TrackingId NULL -> Contents is null, not a Package full of zeroes
```

What "not built" means depends on where the object sits:

- A nested nullable slot becomes `null` (above).
- A nested non-nullable slot propagates the collapse upward: mark it `[InvalidOnNull]` too and the parent collapses as well.
- At the top level, the row behaves like a missing row: `Query<T>` throws, `Query<Optional<T>>` is empty.

```csharp
public record Middle(int Id, [InvalidOnNull] Bottom Bottom) : IDbReadable;
public record Top(int Id, Middle? Middle);
// Bottom's key is NULL -> Bottom collapses -> Middle collapses -> Top.Middle is null
```

Note the difference with [result shapes](../running-queries/result-shapes.md): zero rows is a query-level outcome, a `NULL` column is a slot-level one.

## Behind the attributes

Each rule above is an implementation of one small interface, `INullColHandler`, and the attributes just pick which implementation a slot uses.

| Situation | Handler |
| --- | --- |
| nullable slot, default | `NullableTypeHandle` |
| non-nullable struct, or `[NotNull]` | `NotNullHandle` |
| `[InvalidOnNull]` | `InvalidOnNullAndNullableHandle` / `InvalidOnNullAndNotNullHandle` |

For a behavior they do not cover, implement `INullColHandler` and attach it with an `INullColHandlerMaker`. The `[InvalidOnNull]` handlers are small and are the reference to read first.

The same setting is available without an attribute, by slot name:

```csharp
TypeParsingInfo.GetOrAdd<Invoice>().SetInvalidOnNull("Customer", true);
```
