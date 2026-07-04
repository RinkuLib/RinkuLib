# Reading order

A slot finds its column under one of two regimes. Free: it takes any unconsumed column that matches by name and type, wherever it sits. Sequential: it takes only the column right after the last one consumed. Either way it consumes that column so nothing else can.

The regime comes from the info parsing the type. A normal registration reads free, so order and gaps do not matter.

```csharp
public record Person(int Id, string Name, string? Email = null);

// Columns: Name | Note | Id   ->  Name and Id are each found by name, the gap and order ignored, Email stays null
Person one = cmd.Query<Person>(cnn);
```

`CtorTypeInfo`, the info behind [tuples](../running-queries/result-shapes.md#tuples), reads sequential and hands that down to its elements. Each `Person` takes a consecutive run of columns.

```csharp
// Columns: Id | Name | Id | Name | Email
var (a, b) = cmd.Query<(Person, Person)>(cnn);
// a takes Id(0), Name(1); Email checks only the next column, Id(2), which does not match,
//   and its = null default lets a build anyway, so a never looks as far as Email(4)
// b resumes at Id(2), Name(3), Email(4), seeing only the columns from where a stopped
```

Same type, two regimes, decided by where it is read (see [registering with another info](registration.md#registering-with-another-info)). Two attributes flip the regime on a single slot; on a complex-typed slot they reach into its subtree, [below](#scope-on-a-nested-slot).

## `[CanNotLookAnywhere]`, sequential for one slot

Makes one slot sequential even in a free context: it takes only the column right after the last one consumed, and looks no further if that one does not match.

```csharp
public record Entry(int Id, [CanNotLookAnywhere] int? Code = null);
// Columns: Id | Code          ->  Code takes the next column
// Columns: Id | Other | Code  ->  the next column is "Other", so Code does not match and stays at its default
```

## `[CanLookAnywhere]`, free for one slot

Frees one slot to look anywhere instead of taking the next column in line. Its real use is the first parameter of an object in a sequential run: when a stray column sits between two objects, the second cannot build, because its first parameter lands on that column. Freeing that parameter lets the object anchor past the gap, then read on in sequence from there.

```csharp
public record struct Person(int Id, string Name);
public record struct Address([CanLookAnywhere] int Zip, string City);

// Columns: Id | Name | Note | Zip | City
var (person, address) = cmd.Query<(Person, Address)>(cnn);
// Person reads Id(0), Name(1). Without the flag, Address.Zip would land on Note(2) and the build would fail.
// Freed, Zip skips Note and anchors at Zip(3); City then reads in sequence at City(4).
```

## `[MayReuseCol]`, a column already taken

A slot normally skips a column another slot has consumed. `[MayReuseCol]` lets it take that column anyway.

The usual reason is a [member filled after a constructor](objects.md#post-construction-members): the constructor takes a value to act on it, and a member then needs the same column to store it.

```csharp
public class AuditedRow {
    [CanCompleteWithMembers]
    public AuditedRow(int id) => AccessLog.Record(id);   // the constructor takes id only to run an action
    [MayReuseCol] public int Id { get; set; }             // re-reads the same column to store it
}
// Columns: Id   ->  the constructor consumes Id for its call, then the Id member reuses it
```

Without `[MayReuseCol]` the `Id` member would find its column already taken and stay at its default.

Two constructor parameters can share one column the same way. There the second also needs an [`[Alt]`](names.md) to match the first one's name.

```csharp
public record Money(int Amount, [Alt("Amount")][MayReuseCol] int Copy);
// Columns: Amount   ->  Amount takes the column, Copy reads the same one
```

## Scope on a nested slot

The regime carries down into nested objects, so a reading-order attribute on a complex-typed slot reaches its subtree. The plain attribute reaches only the subtree's first consumed column; a `...Subtree` variant reaches the whole subtree.

`[CanLookAnywhere]` on a complex slot frees that first column, then the subtree reads on in sequence:

```csharp
public record Inner(int A, int? B = null) : IDbReadable;
public record Holder(int Key, [CanLookAnywhere] Inner Data) : IDbReadable;

// Columns: X | Key | Junk | DataA | Gap | DataB
var (x, h) = cmd.Query<(int, Holder)>(cnn);
// the tuple makes Holder sequential; Data.A is freed and found past Junk,
// then Data.B reads in sequence and cannot skip Gap, so it stays null
```

`[CanLookAnywhereSubtree]` frees the whole subtree, so `Data.B` skips `Gap` too:

```csharp
public record Holder(int Key, [CanLookAnywhereSubtree] Inner Data) : IDbReadable;
// Columns: X | Key | Junk | DataA | Gap | DataB   ->  Data.A past Junk, Data.B past Gap
```

`[CanNotLookAnywhere]` and `[MayReuseCol]` pair the same way, each with a `...Subtree` form. `[CanNotLookAnywhereSubtree]` reads a whole nested object in strict column order:

```csharp
public record Outer(int Id, [CanNotLookAnywhereSubtree] Inner Sub);
// Columns: Id | SubA | Gap | SubB   ->  Sub is sequential throughout, so Sub.B cannot skip Gap and stays null
```

## The runtime form

For a type you cannot annotate, replace the slot through `ICanProvideConstructions` with a `ParamInfoPlus` carrying the matching `FlagUpdater`.

```csharp
if (TypeParsingInfo.GetOrAdd<Person>() is ICanProvideConstructions info) {
    var slots = info.PossibleConstructors[0].Parameters;
    var s = slots[1];
    slots[1] = new ParamInfoPlus(s.Type, s.NullColHandler, s.NameComparer,
        FlagUpdater.SequentialRead, IFallbackParserGetter.Nothing);   // [CanNotLookAnywhere] on the second slot
    // FlagUpdater.RemoveSequentialRead is [CanLookAnywhere], FlagUpdater.CanReuse is [MayReuseCol]
    // new FlagUpdater(UsageFlags.RemoveSequentialRead, subtree: true) is the [CanLookAnywhereSubtree] form
}
```
