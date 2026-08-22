# Map rows to objects

A constructor can map columns by name.

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

The root type is requested directly by `Query<Album>`. Nested types follow the registration rules described in [registration](registration.md).

## Writable members

A parameterless type can use writable fields and properties.

```csharp
public sealed class Album
{
    public int Id { get; set; }
    public string? Title { get; set; }
}

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

An `init` member cannot be assigned after parameterless construction.

```csharp
public sealed class Album
{
    public int Id { get; init; }
    public string? Title { get; init; }
}

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
// RINKU3001
```

Use constructor parameters for those members.

## Constructor choice

A usable parameterized constructor is preferred when both a parameterized and parameterless path are available.

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
```

The first construction path that can use the returned columns is selected. See [construction paths](construction-paths.md) for path order and explicit selection.

## Static factories

A static factory can be a construction path.

```csharp
public interface IShape
{
    public static IShape FromCircle(double radius) => new Circle(radius);
}

public record Circle(double Radius) : IShape;

static readonly QueryCommand GetShape = new("SELECT Radius FROM shapes WHERE ShapeId = @shapeId");

IShape shape = GetShape.Query<IShape>(cnn, new { shapeId = 1 });
```

Several factories can support different row shapes.

```csharp
public interface IShape
{
    public static IShape FromCircle(double radius) => new Circle(radius);
    public static IShape FromRectangle(double width, double height) => new Rectangle(width, height);
}

public record Rectangle(double Width, double Height) : IShape;
```

The returned columns decide which usable path wins.

## Optional constructor values

A default constructor parameter can make a shorter row shape valid.

```csharp
public record Album(int Id, string Title, string? Notes = null);

Album shortRow = GetAlbum.Query<Album>(cnn);
Album fullRow = GetAlbumWithNotes.Query<Album>(cnn);
```

Only a real runtime default on the selected construction path provides that fallback.

## Column conversions

Supported CLR conversions can adapt the database column type to the destination slot.

```csharp
public record Totals(long Widened, int Narrowed);

Totals totals = GetTotals.Query<Totals>(cnn);
```

Numeric conversions and user defined conversion operators can participate.

```csharp
public readonly struct AlbumScore
{
    public double Value { get; }

    private AlbumScore(double value) => Value = value;

    public static explicit operator AlbumScore(double value) => new(value);
}

public record RankedAlbum(int Id, AlbumScore Score);
```

Use `[ExactType]` when the database column type must match the slot type.

```csharp
public record Amount([ExactType] int Value);
```

A `long` column cannot fill that slot.

## Fill members after a constructor

A parameterized constructor normally consumes only its parameters.

```csharp
public sealed class Album
{
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}
```

Use `[CanCompleteWithMembers]` when remaining columns should fill writable members.

```csharp
public sealed class Album
{
    [CanCompleteWithMembers]
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}
```

A member does not consume a column already used by the constructor unless its reading rule allows reuse. See [reading order](reading-order.md).

## Recursive objects

A recursive type can keep mapping while matching columns exist.

```csharp
public record User(int Id, string Name, [Alt("Boss")] User? Supervisor = null);

User user = GetUser.Query<User>(cnn);
```

Columns such as `SupervisorId`, `SupervisorName`, `SupervisorBossId`, and `SupervisorBossName` build successive levels.

## Unused columns

Unused columns do not need to be mapped.

```csharp
public record Album(int Id, string Title);

Album album = GetAlbumAndArtistName.Query<Album>(cnn);
// ArtistName is ignored.
```

See [nested objects](nesting.md), [database NULL](nulls.md), and [name rules](names.md) for common object mapping choices.
