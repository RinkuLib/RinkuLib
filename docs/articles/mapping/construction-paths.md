# Construction paths

## Construction negotiation

```csharp
public sealed class Album
{
    public Album(int id, string title) { }
    public Album(int id, string title, string notes) { }
}

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// The two-parameter construction can be satisfied.
```

A construction participates when every required input can be satisfied by the returned shape.

When several constructions can be satisfied, candidates are ordered from the most specific to the least specific. Specificity is based on parameter count and assignment compatibility.

```csharp
public sealed class Value
{
    public Value(object value) { }
    public Value(string value) { }
}

Value value = cnn.Query<Value>("SELECT CAST('Blue' AS varchar(20)) AS value");
// The string construction is more specific than the object construction.
```

If one candidate cannot negotiate a complete value, the next candidate can be tried from the original column usage state.

## Defaults provide another construction

```csharp
public record Album(int Id, string Title, string? Notes = null);

Album shortRow = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
Album longRow = cnn.Query<Album>("SELECT AlbumId AS Id, Title, Notes FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

The declared default can satisfy `Notes` when no matching column exists.

## Recursive termination

```csharp
public record Employee(int Id, string Name, Employee? Manager = null) : IDbReadable;

Employee employee = cnn.Query<Employee>("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerManagerId, b.Name AS ManagerManagerName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId", new { employeeId = 12 });
// Construction keeps taking Manager while matching columns exist.
// At the deepest level the default Manager value provides a terminating construction.
```

A shorter constructor provides the same kind of alternative construction.

```csharp
public sealed class Employee : IDbReadable
{
    public Employee(int id, string name) : this(id, name, null) { }

    public Employee(int id, string name, Employee? manager)
    {
        Id = id;
        Name = name;
        Manager = manager;
    }

    public int Id { get; }
    public string Name { get; }
    public Employee? Manager { get; }
}
```

[Recursive mapping](nesting.md)

## Positional constructor selection

```csharp
public sealed class AlbumRow
{
    public AlbumRow(int id) { }

    [DbConstructor]
    public AlbumRow(int id, string title) { }
}

TypeParsingInfo.AddOrSet<AlbumRow>(CtorTypeInfo.Instance);
```

`CtorTypeInfo` maps constructor parameters by column order and type. `DbConstructor` selects the constructor when several parameterized constructors exist.

## Register a construction

```csharp
ConstructorInfo constructor = typeof(Album).GetConstructor([typeof(int), typeof(string)])
    ?? throw new InvalidOperationException("Album constructor was not found.");

TypeParsingInfo.GetOrAdd<Album>().AddPossibleConstruction(constructor);
```

The same registration surface accepts a reflected static factory.

## Non public members

One construction can be registered explicitly.

```csharp
ConstructorInfo constructor = typeof(Album).GetConstructor(
    BindingFlags.Instance | BindingFlags.NonPublic,
    binder: null,
    [typeof(int), typeof(string)],
    modifiers: null)
    ?? throw new InvalidOperationException("Album constructor was not found.");

TypeParsingInfo.GetOrAdd<Album>().AddPossibleConstruction(constructor);
```

A type can expose its non public constructors and writable members to default discovery.

```csharp
[UsePrivateMembers]
public sealed class Album
{
    private Album(int id, string title)
    {
        Id = id;
        Title = title;
    }

    public int Id { get; }
    public string Title { get; }
}
```

## Complete with members

```csharp
public sealed class Album
{
    [CanCompleteWithMembers]
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// Id is consumed by the constructor.
// Title is filled through the writable member.
```

## Missing column fallback

A mapping slot can provide a parser when no result column matches it.

<xref:Rinku.Mapping.ParamInfo.FallbackTryGetParser*> returns that fallback parser. Returning `null` keeps the slot required.

[Slot rules](../customization/slot-rules.md) · [Type registration](../customization/type-registration.md) · [Reading order](reading-order.md)
