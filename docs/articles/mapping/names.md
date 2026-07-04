# Name matching

A slot matches a column by name, case-insensitive. A nested slot carries a prefix built from the path that reached it, so its columns read as `prefix + name`. Names are matched from the right, one segment at a time.

```csharp
public record Address(int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);

// Columns: Id | HomeZip | HomeCity
// Home is nested, so its columns take the "Home" prefix:
//   Zip  matches HomeZip
//   City matches HomeCity
```

The attributes below adjust which names a slot accepts.

## `[Alt]`, another accepted name

```csharp
public record Person(int Id, [Alt("Name")] string Username);
// Columns: Id | Username   -> fills Username
// Columns: Id | Name       -> fills Username
```

Repeat it for more names:

```csharp
public record Product(int Id, [Alt("Title")][Alt("Label")] string Name);
// a Name, Title, or Label column all fill Name
```

The prefix still applies to an alt on a nested slot:

```csharp
public record Address([Alt("Postal")] int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);
// Columns: Id | HomePostal | HomeCity   -> HomePostal fills Zip
```

## `[AltSkippingSegments]`, match higher up by count

A nested slot can also match a column that drops some of its inner prefix segments. The count is how many segments the name spans: 1 is the normal full prefix, 2 drops the innermost one, and so on.

```csharp
public record Inner([AltSkippingSegments("Code", 2)] int Code) : IDbReadable;
public record Middle(Inner Sub) : IDbReadable;
public record Outer(int Id, Middle Mid);

// Code nests as Mid.Sub, so its full prefix is "MidSub".
// Columns: Id | MidSubCode   -> matches on the full prefix, always available
// Columns: Id | MidCode      -> a span of 2 drops the innermost segment, Sub
```

A span of 3 drops both segments, so a bare `Code` column matches too:

```csharp
public record Inner([AltSkippingSegments("Code", 3)] int Code) : IDbReadable;
public record Middle(Inner Sub) : IDbReadable;
public record Outer(int Id, Middle Mid);

// Columns: Id | Code   -> fills Mid.Sub.Code, both segments dropped
```

## `[AltUpTo]`, match higher up by name

Same idea, but you name the segment to climb back to instead of counting.

```csharp
public record LayerOne(int First, LayerTwo Two);
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third, [AltUpTo("SemiDeep", "Three")] int Deep) : IDbReadable;

// Columns: First | NotTooDeep | SuperDeep | TwoSemiDeep
// Second climbs up to "Two"   and matches NotTooDeep   (prefix cleared)
// Third  climbs up to "Two"   and matches SuperDeep    (prefix cleared)
// Deep   climbs up to "Three" and matches TwoSemiDeep  (prefix left at "Two")
```

## `[NoName]`, position and type only

Drops name matching. The slot takes the next column that fits by type, whatever its name.

```csharp
public readonly struct Boxed<T>([NoName] T value) { public readonly T Value = value; }
// value fills the next column of type T, name ignored
```

The [result shape](../running-queries/result-shapes.md) wrappers use `[NoName]` on their inner value for this reason. `Optional<Track>` matches the same columns as a bare `Track`, because the wrapper adds no name of its own to prefix them.

## The runtime form

For a type you cannot annotate, edit the names on its info. `UpdateAltName` visits every slot, and whatever `INameComparer` you return becomes that slot's names, so every attribute above has a runtime form. Return `null` to leave a slot untouched.

```csharp
var info = TypeParsingInfo.GetOrAdd<Customer>();

// [Alt("PostalCode")]
info.UpdateAltName(nc => nc.GetDefaultName() == "Zip" ? nc.AddAltName("PostalCode") : null);

// [AltSkippingSegments("Code", 2)]
info.UpdateAltName(nc => nc.GetDefaultName() == "Code" ? nc.AddComparer(new NameMultiSpan("Code", 2)) : null);

// [AltUpTo("SemiDeep", "Three")]
info.UpdateAltName(nc => nc.GetDefaultName() == "Deep" ? nc.AddComparer(new NameMultiSpanKey("SemiDeep", "Three")) : null);

// [NoName]
info.UpdateAltName(nc => nc.GetDefaultName() == "Value" ? NoNameComparer.Instance : null);
```

`AddAltName` and `AddComparer` add to a slot's current names; returning a comparer on its own (like `NoNameComparer.Instance` above) replaces them. `RemoveName` and `RemoveComparer` take one back out:

```csharp
// undo the [Alt("PostalCode")] above
info.UpdateAltName(nc => nc.GetDefaultName() == "Zip" ? nc.RemoveName("PostalCode") : null);

// undo the [AltUpTo("SemiDeep", "Three")] above
info.UpdateAltName(nc => nc.GetDefaultName() == "Deep" ? nc.RemoveComparer(new NameMultiSpanKey("SemiDeep", "Three")) : null);
```
