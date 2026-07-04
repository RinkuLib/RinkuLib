# Name matching

A slot's names are always matched as `[prefix] + [candidate]`, the prefix being the path of nesting that led to it. The attributes adjust the candidates:

- `[Alt("Name")]` adds an alternative name.
- `[AltSkippingSegments("Name", n)]` adds an alternative that skips `n` prefix segments, letting a deep slot match a higher-level column.
- `[AltUpTo("Name", "Key")]` skips prefix segments back up to the named segment.
- `[NoName]` drops name matching, position and type only.

```csharp
public record LayerOne(int First, LayerTwo Two);
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third, [AltUpTo("SemiDeep", "Three")] int Deep) : IDbReadable;

// Columns: First | NotTooDeep | SuperDeep | TwoSemiDeep
// Second matches NotTooDeep (prefix rewound to "Two"), Third matches SuperDeep,
// Deep matches TwoSemiDeep (rewound to after "Three").
```

`[NoName]` is how the [result shape](../running-queries/result-shapes.md) wrappers wrap their inner value without inventing a column name. Your own single-value wrapper does the same:

```csharp
public readonly struct Boxed<T>([NoName] T value) { public readonly T Value = value; }
```

The runtime form, for a type you cannot annotate, edits the names on the type's info:

```csharp
var info = TypeParsingInfo.GetOrAdd<Customer>();

info.UpdateAltName(nc =>                  // visits every slot's names
    nc.GetDefaultName().Equals("Zip", StringComparison.OrdinalIgnoreCase)
        ? nc.AddAltName("PostalCode")     // what [Alt("PostalCode")] would have done
        : null);                          // null leaves the slot as is
```
