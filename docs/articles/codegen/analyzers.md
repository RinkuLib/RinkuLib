# Analyzers and code fixes

The `Rinku` package includes its C# analyzers and code fixes. There is no separate analyzer package to install.

Rinku Power Tools is not required for the analyzers. PowerTools does make the schema workflow useful by adding `<Schema>` metadata to generated result records.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumResult(int Id, string Title);
```

`BasedOn` tracks when a referenced schema was last reviewed. `MatchConstructor` keeps a constructor compatible with a type or method. The invocation analyzer completes an uncalled method reference.

## Add a schema link

When the current project contains at least one source declaration with `<Schema>`, a type without a schema link gets an `Add schema link` Quick Action.

![Rinku analyzer Quick Action](../../images/codegen/analyzer-quick-action.png)

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumResult(int Id, string Title);

public record AlbumDto(int Id, string Title);
```

The action offers two kinds of link.

```text
Track schema changes
Require a matching constructor
```

Choosing `Track schema changes` adds `BasedOn` with the current schema timestamp.

```csharp
/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

Choosing `Require a matching constructor` adds `MatchConstructor`.

```csharp
/// <MatchConstructor cref="GetAlbumResult" />
public record AlbumDto(int Id, string Title);
```

The picker includes source types and methods marked with `<Schema>` in the current project compilation. A manual `cref` can also be written directly.

The automatic add action is not offered on a type that already has `<Schema>`, `<BasedOn>`, or `<MatchConstructor>`.

## Track reviewed schemas with BasedOn

`BasedOn` records that an application type was reviewed against a particular version of another declaration.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumResult(int Id, string Title);

/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

If the referenced schema gets a newer timestamp, `RK0100` warns on the `BasedOn` link.

```csharp
/// <Schema LastUpdated="2026-08-22T09:30Z" />
public partial record GetAlbumResult(int Id, string Title, int? ReleaseYear);

/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title); // RK0100
```

Review and update the application type yourself when needed.

```csharp
/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title, int? ReleaseYear);
```

Then use `Acknowledge current schema`. The action changes only the `LastUpdated` value.

```csharp
/// <BasedOn cref="GetAlbumResult" LastUpdated="2026-08-22T09:30Z" />
public record AlbumDto(int Id, string Title, int? ReleaseYear);
```

A missing `LastUpdated` also raises `RK0100` when the referenced declaration has a schema timestamp.

```csharp
/// <Schema LastUpdated="2026-08-22T09:30Z" />
public partial record GetAlbumResult(int Id, string Title);

/// <BasedOn cref="GetAlbumResult" />
public record AlbumDto(int Id, string Title); // RK0100
```

A `BasedOn` link can point to a type or method. Timestamp tracking works when the referenced source declaration carries `<Schema LastUpdated="..." />` in the current compilation.

Several `BasedOn` links may be written on the same type. Each link is checked independently.

### Scaffold from a BasedOn link

An existing `BasedOn` link also exposes constructor generation actions when the target does not already contain the referenced constructor shape.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public class AlbumSchema
{
    public AlbumSchema(int id, string? title) { }
}

/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public class AlbumDto
{
}
```

`Add constructor from AlbumSchema` produces the constructor shape.

```csharp
/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public class AlbumDto
{
    public AlbumDto(int id, string? title)
    {
    }
}
```

When every generated property name is available and there is no `out` parameter, `Add constructor and properties` is also available.

```csharp
/// <BasedOn cref="AlbumSchema" LastUpdated="2026-08-21T14:00Z" />
public class AlbumDto
{
    public AlbumDto(int id, string? title)
    {
        Id = id;
        Title = title;
    }

    public int Id { get; set; }
    public string? Title { get; set; }
}
```

`BasedOn` itself does not require a constructor match. Constructor generation on a `BasedOn` link is a convenience action. `RK0100` is still based only on schema timestamps.

## Require a constructor with MatchConstructor

`MatchConstructor` is the strict shape contract.

```csharp
public record AlbumSchema(int Id, string? Title);

/// <MatchConstructor cref="AlbumSchema" />
public record AlbumDto(int Id, string? Title);
```

A referenced type may expose several instance constructors. Matching any one of them satisfies that link.

```csharp
public class AlbumSchema
{
    public AlbumSchema(int id) { }
    public AlbumSchema(int id, string title) { }
}

/// <MatchConstructor cref="AlbumSchema" />
public class AlbumDto
{
    public AlbumDto(int id, string title) { }
}
```

An implicit parameterless constructor also counts.

```csharp
public class EmptySchema { }

/// <MatchConstructor cref="EmptySchema" />
public class EmptyDto { }
```

A method can provide the required parameter shape too.

```csharp
public static class AlbumSchemas
{
    public static object Read(ref int id, params string?[] titles) => new();
}

/// <MatchConstructor cref="AlbumSchemas.Read" />
public class AlbumDto
{
    public AlbumDto(ref int id, params string?[] titles) { }
}
```

The comparison requires all of the following to match.

```text
parameter count
parameter order
parameter names
parameter types including reference nullability
ref out and in
params
```

Parameter attributes and default values do not affect the comparison.

```csharp
public class AlbumSchema
{
    public AlbumSchema(int id = 1) { }
}

/// <MatchConstructor cref="AlbumSchema" />
public class AlbumDto
{
    public AlbumDto(int id = 2) { }
}
```

A mismatch raises `RK0101` and offers fixes for the missing generated members.

```csharp
public record AlbumSchema(int Id, string Title);

/// <MatchConstructor cref="AlbumSchema" />
public record AlbumDto(string Title, int AlbumId); // RK0101
```

Several `MatchConstructor` links are checked independently. The target must satisfy every link.

```csharp
public record IdSchema(int Id);
public record TitleSchema(string Title);

/// <MatchConstructor cref="IdSchema" />
/// <MatchConstructor cref="TitleSchema" />
public class AlbumDto
{
    public AlbumDto(int Id) { }
    public AlbumDto(string Title) { }
}
```

### Generate the missing constructor

`RK0101` offers a constructor Quick Action when adding the referenced signature would be legal C#.

The generated constructor preserves parameter names, types, nullability, `ref`, `out`, `in`, `params`, and parameter attributes.

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SlotAttribute(string name) : Attribute
{
    public bool Required { get; set; }
}

public static class AlbumSchemas
{
    public static void Read([Slot("id", Required = true)] ref int id, params string?[] titles) { }
}

/// <MatchConstructor cref="AlbumSchemas.Read" />
public class AlbumDto
{
}
```

The generated constructor keeps the parameter contract.

```csharp
public class AlbumDto
{
    public AlbumDto([Slot("id", Required = true)] ref int id, params string?[] titles)
    {
    }
}
```

Default values are not part of the match and are not copied by constructor generation.

The constructor and properties action is available only when the generated properties would not conflict with existing members and there is no `out` parameter.

If the target already has the same C# constructor signature with different parameter names, `RK0101` can remain without an add constructor action. C# cannot add another constructor that differs only by parameter names.

## Complete a method invocation

`RK0002` finds a method reference inside a method, constructor, or local function when that reference is not already invoked.

```csharp
int Save(int id) => id;

int Build(int id) => Save;
```

`Generate invocation` reuses a matching value that is already in scope.

```csharp
int Save(int id) => id;

int Build(int id) => Save(id);
```

Matching values use the same parameter name ignoring case and the same CLR type.

The fix can reuse locals, parameters, fields, properties, and public members of values that are already in scope.

```csharp
public record SaveAlbumRequest(int Id, string Title);

int Save(int id, string title) => id;

int Build(SaveAlbumRequest request) => Save;
```

The generated invocation can use the request members directly.

```csharp
int Save(int id, string title) => id;

int Build(SaveAlbumRequest request) => Save(request.Id, request.Title);
```

When no matching value exists, the missing value is added to the caller and passed through.

```csharp
int Save(int id, string title) => id;

int Build(int id) => Save;
```

The completed method invocation is shown below.

```csharp
int Save(int id, string title) => id;

int Build(int id, string title) => Save(id, title);
```

New caller parameters preserve `ref`, `out`, `in`, and `params` from the called method.

The same method invocation completion also supports member access expressions.

```csharp
public sealed class AlbumService
{
    public int Save(int id) => id;
}

int Build(AlbumService service, int id) => service.Save;
```

The completed member invocation is shown below.

```csharp
int Build(AlbumService service, int id) => service.Save(id);
```

Normal invocations, `nameof`, and delegate conversions are ignored.

```csharp
int Save() => 1;

void Build()
{
    Func<int> callback = Save;
    int value = Save();
    string name = nameof(Save);
}
```

When overload resolution exposes several method candidates, the Quick Action can offer an invocation choice for each candidate.

Method invocation completion is not tied to CodeGen or database code.

## Schema metadata

`<Schema>` is an XML documentation tag used by the analyzer workflow.

PowerTools writes it on generated result records.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumsResult(int Id, string Title);
```

PowerTools preserves the existing generated record when its inspected result columns are unchanged. That preserves the existing timestamp too. A changed generated result shape gets a new timestamp.

Source code can also mark its own type or method as a schema.

```csharp
public static class AlbumQueries
{
    /// <Schema LastUpdated="2026-08-21T14:00Z" />
    public static object ReadAlbum(int id, string title) => new();
}
```

The automatic schema picker searches source declarations in the current project compilation.

`MatchConstructor` can still be written manually against another resolvable type or method even when that declaration does not carry `<Schema>`.

`BasedOn` needs a source `<Schema LastUpdated="..." />` declaration for stale timestamp detection. A referenced assembly does not expose source documentation tags to this analyzer workflow.

## Configure warnings

`RK0100` and `RK0101` are warnings. Normal Roslyn configuration can change their severity.

```ini
[*.cs]
dotnet_diagnostic.RK0100.severity = none
dotnet_diagnostic.RK0101.severity = error
```

The `RK0000`, `RK0001`, and `RK0002` rules are hidden. They exist to expose Quick Actions and do not normally need severity configuration.

## Diagnostic reference

| Rule | Severity | Meaning |
| --- | --- | --- |
| `RK0000` | Hidden | Exposes actions for an existing `BasedOn` link |
| `RK0001` | Hidden | Exposes the action that adds a schema link |
| `RK0002` | Hidden | Exposes method invocation generation |
| `RK0100` | Warning | A `BasedOn` acknowledgement is older than the referenced schema |
| `RK0101` | Warning | No target constructor matches the referenced type or method |

## Code fix reference

| Action | Available from | Result |
| --- | --- | --- |
| `Add schema link` | `RK0001` | Adds either `BasedOn` or `MatchConstructor` for a selected source schema |
| `Acknowledge current schema` | `RK0000` | Updates only the `BasedOn` timestamp when the referenced schema is newer |
| `Add constructor from ...` | `RK0000` or `RK0101` | Adds the referenced constructor or method parameter shape |
| `Add constructor and properties from ...` | `RK0000` or `RK0101` | Adds the constructor plus assignable properties when that shape is legal |
| `Generate invocation` | `RK0002` | Invokes the selected method, reuses matching values, and threads missing parameters through the caller |

`RK0100` and the `Acknowledge current schema` action appear on the same stale `BasedOn` relationship through separate analyzer rules. The visible warning reports the stale contract while the hidden rule supplies the action.

See [Generated commands](generated-code.md) for the PowerTools result records that produce `<Schema>` metadata.
