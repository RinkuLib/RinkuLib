# Nullability

*What the mapper does when a column is `NULL`.*

Every slot (a constructor parameter or a settable member) has a rule for what to do when its column comes back `NULL`. There's a sensible default, two attributes for the common adjustments, and underneath them an interface you can implement when you need something bespoke. Most code never goes past the default and the two attributes.

## The default

With no annotations, nullability follows the C# type.

- A **reference type** or `Nullable<T>` accepts `NULL` and receives `null`.
- A **non-nullable struct** rejects `NULL`. If the column is null the mapper throws, because there's no valid value to give it.

```csharp
public record Track(int Id, string Name, string? Composer);
// Id (int)  -> NULL would throw. An int can't be null
// Name      -> NULL allowed, you get null
// Composer  -> NULL allowed, you get null
```

So you get what the type signature implies, and you rarely think about it.

## `[NotNull]`, reject null for a nullable slot

Put `[NotNull]` (the standard `System.Diagnostics.CodeAnalysis.NotNullAttribute`) on a reference-type or `Nullable<T>` slot to make the mapper **throw on `NULL`** instead of handing you `null`. Use it when a column is nominally nullable in the schema but you treat its absence as a bug.

```csharp
public record Track(int Id, [NotNull] string Name);   // a null Name is an error; fail loudly
```

## `[InvalidOnNull]`, let a null collapse the object

`[InvalidOnNull]` on a slot says that if this value is `null`, **abandon the construction path** rather than build a half-empty object. The whole object becomes "not present", and the caller decides what that means. A top-level `Query<Invoice>` would see no object, an `Optional<Invoice>` would be empty, and a nested `Invoice` member would itself be set to null.

This is what you want for an optional nested object from an outer join, where a missing match leaves its columns all `NULL`.

```csharp
public record Invoice(int Id, decimal Total, [InvalidOnNull] Customer Customer);
// If the joined Customer columns are all NULL, you don't get an Invoice with a broken
// Customer. The Customer (and, depending on how you asked, the Invoice) is treated as absent.
```

## When the defaults are not enough

The behaviors above aren't hard-wired. Each is an implementation of one small interface, `INullColHandler`, and the attributes just select which implementation a slot uses.

| Slot situation | Handler used |
| --- | --- |
| nullable, default | `NullableTypeHandle` (pass `null` through) |
| non-nullable struct, or `[NotNull]` | `NotNullHandle` (throw on null) |
| `[InvalidOnNull]` | `InvalidOnNullAndNullableHandle` / `InvalidOnNullAndNotNullHandle` (abandon the path) |

To do something they don't, implement `INullColHandler` (the check, throw, or abandon logic) and an `INullColHandlerMaker` (a small factory that attaches it to a slot during discovery). The built-ins are ordinary implementations of the same hook you'd extend, the `[InvalidOnNull]` handlers are a good, tiny example to read if you ever do.

You can also set this without an attribute, by default name, with `TypeParsingInfo.SetInvalidOnNull(name, true)` (see [custom parsing & matching](custom-parsing.md)).
