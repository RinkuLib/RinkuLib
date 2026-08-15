# Code-generation analyzers

The `Rinku` package includes analyzers for generated schemas, matching constructors, and incomplete method calls.

## Detect a stale derived schema

`BasedOn` records the timestamp of the schema an application type was reviewed against.

```csharp
/// <Schema LastUpdated="2026-08-11T10:00Z" />
public record CustomerSchema(int Id, string? Name);

/// <BasedOn cref="CustomerSchema" LastUpdated="2026-08-11T10:00Z" />
public record CustomerDto(int Id, string? Name);
```

When the source schema changes, the older link raises `RK0100`.

```csharp
/// <Schema LastUpdated="2026-08-12T09:00Z" />
public record CustomerSchema(int Id, string? Name, bool Active);

/// <BasedOn cref="CustomerSchema" LastUpdated="2026-08-11T10:00Z" />
public record CustomerDto(int Id, string? Name); // RK0100
```

The Quick Action updates the acknowledged timestamp after the application type has been reviewed.

Disable that warning through `.editorconfig` when the project does not want it.

```ini
dotnet_diagnostic.RK0100.severity = none
```

## Require a matching constructor

`MatchConstructor` requires one target constructor to match a referenced type or method.

```csharp
public record CustomerSchema(int Id, string? Name);

/// <MatchConstructor cref="CustomerSchema" />
public record CustomerDto(int Id, string? Name);
```

A referenced type may expose several constructors. Matching any one is sufficient.

```csharp
public class CustomerSchema {
    public CustomerSchema(int id) { }
    public CustomerSchema(int id, string name) { }
}

/// <MatchConstructor cref="CustomerSchema" />
public record CustomerDto(int id, string name);
```

A method can provide the shape too.

```csharp
public static class CustomerSchemas {
    public static object Read(ref int id, params string?[] names) => new();
}

/// <MatchConstructor cref="CustomerSchemas.Read" />
public class CustomerDto {
    public CustomerDto(ref int id, params string?[] names) { }
}
```

Parameter order, names, types, nullability, `ref`, `out`, `in`, and `params` must match. Attributes and default values do not affect the comparison.

The constructor Quick Action can copy the referenced parameters and their attributes when no conflicting constructor exists.

## Complete a method call

An uncalled method reference inside a method offers an invocation Quick Action.

```csharp
int Save(int id) => id;

int Build(int id) => Save;
// Quick Action: int Build(int id) => Save(id);
```

Missing values become parameters on the calling method.

```csharp
int Save(int id) => id;

int Build() => Save;
// Quick Action: int Build(int id) => Save(id);
```

Method groups used as delegates remain unchanged.

## Diagnostic reference

| Rule | Severity | Meaning |
| --- | --- | --- |
| `RK0000` | Hidden | Offers actions for a `BasedOn` link. |
| `RK0001` | Hidden | Offers to add a schema link. |
| `RK0002` | Hidden | Offers to complete a method call. |
| `RK0100` | Warning | A `BasedOn` link is older than its schema. |
| `RK0101` | Warning | No constructor matches the referenced type or method. |
