# Object mapping

## Constructor mapping

```csharp
public record Album(int Id, string Title);

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

Constructor inputs are matched against returned columns.

## Writable members

```csharp
public sealed class Album
{
    public int Id { get; set; }
    public string? Title { get; set; }
}

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

A parameterless construction can fill writable fields and properties.

An `init` member cannot be assigned after parameterless construction.

```csharp
public sealed class Album
{
    public int Id { get; init; }
    public string? Title { get; init; }
}

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
// RINKU3001
```

[RINKU3001](../reference/errors.md#rinku3001-no-parser-for-the-schema)

## Several construction paths

```csharp
public sealed class Album
{
    public Album() { }

    public Album(int id, string title)
    {
        Id = id;
        Title = title;
    }

    public int Id { get; }
    public string Title { get; } = "";
}

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

A path participates when its required inputs can be satisfied by the returned shape.

[Construction paths](construction-paths.md)

## Static factories

```csharp
public interface IShape
{
    public static IShape FromCircle(double radius) => new Circle(radius);
    public static IShape FromRectangle(double width, double height) => new Rectangle(width, height);
}

public record Circle(double Radius) : IShape;
public record Rectangle(double Width, double Height) : IShape;

IShape circle = cnn.Query<IShape>("SELECT Radius FROM shapes WHERE ShapeId = @shapeId", new { shapeId = 1 });
IShape rectangle = cnn.Query<IShape>("SELECT Width, Height FROM shapes WHERE ShapeId = @shapeId", new { shapeId = 2 });
```

The returned shape determines which construction can participate.

## Alternative construction

```csharp
public record Album(int Id, string Title, string? Notes = null);

Album shortRow = cnn.Query<Album>("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
Album fullRow = cnn.Query<Album>("SELECT AlbumId AS Id, Title, Notes FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

The declared default provides a construction path when `Notes` is absent.

The same mechanism lets a recursive shape terminate.

```csharp
public record Employee(int Id, string Name, [Alt("Boss")] Employee? Manager = null) : IDbReadable;

Employee employee = cnn.Query<Employee>("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerBossId, b.Name AS ManagerBossName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId", new { employeeId = 12 });
// ManagerId maps another Employee.
// ManagerBossId maps that Employee.Manager through Alt("Boss").
// The deepest Employee finishes through the construction where Manager uses its default.
```

[Recursive mapping](nesting.md) · [Construction paths](construction-paths.md)

## Complete with writable members

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
// Title fills the remaining writable member.
```

[Reading order](reading-order.md)

## Conversions

```csharp
public record Totals(long Widened, int Narrowed);

Totals totals = cnn.Query<Totals>("SELECT CAST(12 AS int) AS Widened, CAST(34 AS bigint) AS Narrowed");
```

Supported CLR conversions can participate in a slot.

```csharp
public readonly struct AlbumScore
{
    public double Value { get; }
    private AlbumScore(double value) => Value = value;
    public static explicit operator AlbumScore(double value) => new(value);
}

public record RankedAlbum(int Id, AlbumScore Score);

RankedAlbum album = cnn.Query<RankedAlbum>("SELECT AlbumId AS Id, Score FROM album_scores WHERE AlbumId = @albumId", new { albumId = 12 });
```

`[ExactType]` removes conversion for that slot.

```csharp
public record Amount([ExactType] int Value);
// A returned long column cannot fill Value.
```

## Unused columns

```csharp
public record Album(int Id, string Title);

Album album = cnn.Query<Album>("SELECT AlbumId AS Id, Title, ArtistName FROM album_search WHERE AlbumId = @albumId", new { albumId = 12 });
// ArtistName remains unused.
```

[Name adaptation](names.md) · [Database NULL](nulls.md)
