# Analyzers and code fixes

The analyzers ship with the `Rinku` package. They use two different pieces of metadata:

- `BasedOn` selects the runtime schema member.
- `Schema` records a source version for analyzer checks only. It has no runtime meaning.

## Start with a runtime schema link

```csharp
using Rinku.Mapping;

public static class AlbumSchemas
{
    public static void Read(int Id, string Title) { }
}

[BasedOn(typeof(AlbumSchemas), null, nameof(AlbumSchemas.Read))]
public record AlbumDto(int Id, string Title);
```

`BasedOn` can be discovered at runtime through `SchemaReference.GetSchemas()` and passed directly to
the parser. The analyzer also uses it to compare the result constructor with the selected schema
shape.

## Add a link with a Quick Action

```csharp
[Schema("2026-08-21T14:00Z")]
public record AlbumSchema(int Id, string Title);

public record AlbumDto(int Id, string Title);
```

When a source `Schema` is available, the Quick Action adds a `BasedOn` attribute. A type that already
has `BasedOn` metadata is not offered another link action.

The generated attribute uses ordinary C# type and member references, so `typeof` and `nameof` remain
available for navigation and refactoring:

```csharp
[BasedOn(typeof(AlbumSchema))]
public record AlbumDto(int Id, string Title);
```

## Select a constructor or method

The one-argument form uses the referenced type's constructor schema:

```csharp
[BasedOn(typeof(AlbumSchema))]
public record AlbumDto(int Id, string Title);
```

The analyzer accepts any constructor on the referenced type that exactly matches any target
constructor. Runtime discovery returns every constructor as a schema candidate.

Select methods with `nameof`:

```csharp
public static class AlbumSchemas
{
    public static object Read(int Id, string Title) => new();
}

[BasedOn(typeof(AlbumSchemas), null, nameof(AlbumSchemas.Read))]
public record AlbumDto(int Id, string Title);
```

The method form includes every overload with that name. Runtime discovery returns each candidate
`MemberInfo`; it does not use XML documentation.

## Version acknowledgement

Put `Schema` on the source and pass `lastUpdated` to `BasedOn` when the source has a versioned shape. Both attributes parse the constructor string into a read-only UTC `DateTime?`:

`BasedOn` accepts `(Type declaringType, string? lastUpdated = null, string? methodName = null)`. A type and positional timestamp have the same meaning as a named `lastUpdated` argument. For a method reference without a timestamp, pass `null` before the method name.

```csharp
[Schema("2026-08-22T09:30Z")]
public record AlbumSchema(int Id, string Title, int? ReleaseYear);

[BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-22T09:30Z")]
public record AlbumDto(int Id, string Title, int? ReleaseYear);
```

The link is current when its date is equal to or newer than the source date. `RK0100` reports a link
whose date is older than the source. The acknowledgement action updates only `BasedOn.LastUpdated`.

When the source has a `Schema.LastUpdated` value, `Acknowledge current schema` is available from the
`BasedOn` actions even when the constructor already matches. It adds the `lastUpdated` constructor argument when it is
missing, or updates it to the current source date. The action is therefore independent of `RK0101`.

If the source has no version date, there is no version acknowledgement. The analyzer instead requires
the schema candidates to have a matching constructor shape. When the source is versioned but the
link has no date, the same exact-match rule applies and the `BasedOn` actions offer an acknowledgement
that records the current source date.

If the source date is newer than the link date, the analyzer requires the match again. A changed shape
then reports both the stale-link warning (`RK0100`) and the constructor warning (`RK0101`). An equal or
newer link date explicitly acknowledges the changed shape, so `RK0101` is not reported.

```csharp
public record AlbumSchema(int Id, string Title);

[BasedOn(typeof(AlbumSchema))]
public record AlbumDto(long Id, string Title); // RK0101
```

An acknowledged shape change is intentional and therefore does not produce `RK0101`:

```csharp
[Schema("2026-08-22T09:30Z")]
public record AlbumSchema(int Id, string Title);

[BasedOn(typeof(AlbumSchema), lastUpdated: "2026-08-22T09:30Z")]
public record AlbumDto(int Id); // no RK0101; the link acknowledges the new shape
```

If `AlbumDto.LastUpdated` were earlier than the source date, the same example would require the
constructor match again and would report `RK0100` and `RK0101`.

## Constructor matching

The constructor check compares count, order, names, types, reference nullability, `ref`/`out`/`in`,
and `params`. Default values do not affect the match. A constructor reference compares every source
constructor with every target constructor until one pair matches. A method reference does the same
with every method overload having the selected name.

```csharp
public class AlbumSchema
{
    public AlbumSchema(int id) { }
    public AlbumSchema(int id, string title) { }
}

[BasedOn(typeof(AlbumSchema))]
public class AlbumDto
{
    public AlbumDto(int id, string title) { }
}
```

The selected constructor or method can also require `ref` and `params`:

```csharp
public static class AlbumSchemas
{
    public static object Read(ref int id, params string?[] titles) => new();
}

[BasedOn(typeof(AlbumSchemas), null, nameof(AlbumSchemas.Read))]
public class AlbumDto
{
    public AlbumDto(ref int id, params string?[] titles) { }
}
```

`RK0101` offers a constructor code fix. The generated constructor preserves the selected candidate's
parameter contract and can also generate assignable properties when that option is legal.

## Generated result metadata

Generated result records use the schema attribute in the shared PowerTools support file:

```csharp
using Schema = RinkuPowerTools.Generated.SchemaAttribute;

[Schema("2026-08-21T14:00Z")]
public partial record GetAlbumsResult(int Id, string Title);
```

`SchemaAttribute` parses the constructor's timestamp string into a UTC `DateTime`.
Generated files remain independent of Rinku. The analyzer recognizes both the generated schema
attribute and `Rinku.Mapping.SchemaAttribute`. Application-owned result types use the existing
`Rinku.Mapping.BasedOnAttribute`; PowerTools does not generate another `BasedOn` attribute.

## Complete a method invocation

`RK0002` finds an uncalled method reference in a method, constructor, or local function.

```text
int Save(int id) => id;

int Build(int id) => Save; // RK0002
```

`Generate invocation` reuses a matching value already in scope:

```csharp
int Save(int id) => id;

int Build(int id) => Save(id);
```

Members of an in-scope value can also satisfy parameters:

```csharp
public record SaveAlbumRequest(int Id, string Title);

int Save(int id, string title) => id;

int Build(SaveAlbumRequest request) => Save(request.Id, request.Title);
```

When a value is missing, the fix can add it to the caller and pass it through:

```csharp
int Save(int id, string title) => id;

int Build(int id, string title) => Save(id, title);
```

Normal invocations, `nameof`, and delegate conversions are not changed.

## Severity configuration

```ini
[*.cs]
dotnet_diagnostic.RK0100.severity = warning
dotnet_diagnostic.RK0101.severity = error
dotnet_diagnostic.RK0002.severity = suggestion
```

The standard `.editorconfig` diagnostic severity settings apply.

## Diagnostic reference

| Rule | Severity | Meaning |
| --- | --- | --- |
| `RK0000` | Hidden | Offers actions for a `BasedOn` link |
| `RK0001` | Hidden | Offers to add a `BasedOn` link |
| `RK0002` | Hidden | Offers to complete a method call |
| `RK0100` | Warning | A versioned `BasedOn` link is older than its `Schema` source |
| `RK0101` | Warning | No constructor matches the referenced type or method |

## Code fix reference

| Action | Available from | Result |
| --- | --- | --- |
| `Add schema link` | `RK0001` | Adds `BasedOn` for a selected source schema |
| `Acknowledge current schema` | `RK0000` or `RK0100` | Updates `BasedOn.LastUpdated` |
| `Add constructor from ...` | `RK0000` or `RK0101` | Adds the referenced parameter shape |
| `Add constructor and properties from ...` | `RK0000` or `RK0101` | Adds the constructor and assignable properties when legal |
| `Generate invocation` | `RK0002` | Invokes the selected method and threads missing parameters through the caller |

[Generated commands](generated-code.md)
