# Map rows to objects

This page covers default object mapping through `TypeParsingInfo` and construction paths. You can change one rule, replace a type registration, or use another result parser.

## Constructor

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbum = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

An `init` member cannot be filled after a parameterless construction.

```csharp
public sealed class Album {
    public int Id { get; init; }
    public string? Title { get; init; }
}

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
// RINKU3001. Use constructor parameters for the init members.
```

## Writable members

```csharp
public sealed class Album {
    public int Id { get; set; }
    public string? Title { get; set; }
}

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

A usable parameterized constructor is preferred when both forms exist.

```csharp
public sealed class Album {
    public Album() { }

    public Album(int id, string title) {
        Id = id;
        Title = title;
    }

    public int Id { get; }
    public string Title { get; } = "";
}

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

## Static factory

```csharp
public interface IShape {
    public static IShape FromCircle(double radius) => new Circle(radius);
}

public record Circle(double Radius) : IShape;

static readonly QueryCommand GetShape = new("SELECT Radius FROM shapes WHERE ShapeId = @shapeId");

IShape shape = GetShape.Query<IShape>(cnn, new { shapeId = 1 });
// shape is a Circle
```

Several factories can provide different implementations.

```csharp
public interface IShape {
    public static IShape FromCircle(double radius) => new Circle(radius);

    public static IShape FromRectangle(double width, double height) => new Rectangle(width, height);
}

public record Circle(double Radius) : IShape;
public record Rectangle(double Width, double Height) : IShape;

IShape circle = GetCircle.Query<IShape>(cnn);
// Radius -> Circle

IShape rectangle = GetRectangle.Query<IShape>(cnn);
// Width | Height -> Rectangle
```

The first factory or constructor that can use the returned columns wins. See [construction paths](construction-paths.md) for the full selection and configuration rules.

## More than one row shape

A type can accept a shorter or longer result.

```csharp
public record Album(int Id, string Title, string? Notes = null);

Album shortRow = GetAlbum.Query<Album>(cnn);
// Id | Title -> Album(Id, Title, null)

Album fullRow = GetAlbumWithNotes.Query<Album>(cnn);
// Id | Title | Notes -> Album(Id, Title, Notes)
```

This covers the same two shapes as two constructors.

```csharp
public sealed class Album {
    public Album(int id, string title) { }
    public Album(int id, string title, string? notes) { }
}
```

Only the runtime type’s default value provides this fallback.

```csharp
public record RatedAlbum(int Id, int Rating = 5);

RatedAlbum album = GetAlbumIdOnly.Query<RatedAlbum>(cnn);
// RINKU3001. The missing Rating does not become 5.
```

The [custom fallback example](construction-paths.md#supply-a-custom-fallback) shows how to provide a deliberate mapping fallback.

## Column type conversions

An exact type match is used directly. Otherwise Rinku can apply the supported CLR conversion path, including numeric widening and narrowing and user-defined implicit or explicit conversion operators.

```csharp
public record Totals(long Widened, int Narrowed);

Totals totals = GetTotals.Query<Totals>(cnn);
// An int column uses an implicit numeric conversion for Widened.
// A long column uses an explicit numeric conversion for Narrowed.
```

User-defined implicit and explicit operators participate in the same conversion path.

```csharp
public readonly struct AlbumScore {
    public double Value { get; }
    private AlbumScore(double value) => Value = value;
    public static explicit operator AlbumScore(double value) => new(value);
}

public record RankedAlbum(int Id, AlbumScore Score);

RankedAlbum album = GetRankedAlbum.Query<RankedAlbum>(cnn);
// A double Score column uses AlbumScore's explicit operator.
```

A nullable destination preserves database `NULL` and applies the conversion only to non-null values. Unsupported conversions make that construction path unusable. Register [custom scalar reading](../customization/type-registration.md#register-custom-scalar-reading) for application-specific conversions that the normal CLR path does not cover.

### Require the exact column type

`[ExactType]` requires the column type to match the slot type.

```csharp
public record Amount([ExactType] int Value);

Amount amount = GetLongValue.Query<Amount>(cnn);
// RINKU3001. A long column cannot fill the exact int slot.
```

Nullable value types compare their underlying type.

```csharp
public record Amount([ExactType] int? Value);
// An int column is valid. A long column is not.
```

## Fill members after construction

A parameterized constructor consumes its parameters only.

```csharp
public sealed class Album {
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}

Album album = GetAlbum.Query<Album>(cnn);
// Id fills the constructor. Title remains unset.
```

`[CanCompleteWithMembers]` lets remaining columns fill writable members.

```csharp
public sealed class Album {
    [CanCompleteWithMembers]
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}

Album album = GetAlbum.Query<Album>(cnn);
// Id fills the constructor. Title fills the property.
```

A member does not overwrite a column already consumed by the constructor.

```csharp
public sealed class Album {
    [CanCompleteWithMembers]
    public Album(int id) => Id = id;

    public int Id { get; set; }
    public string? Title { get; set; }
}
// Id remains the constructor value. Title can use the remaining column.
```

Use [`[MayReuseCol]`](reading-order.md#reuse-a-column) when a member must read an already-consumed column.

## Recursive objects

A self-referencing type reads as many levels as the columns provide.

```csharp
public record User(int Id, string Name, [Alt("Boss")] User? Supervisor = null);

User user = GetUser.Query<User>(cnn);
// Id | Name | SupervisorId | SupervisorName | SupervisorBossId | SupervisorBossName
```

```text
user
└─ Supervisor
   └─ Supervisor
      └─ Supervisor = null
```

The default `= null` lets the final level finish when no more supervisor columns remain.

## Unused columns

Columns that the selected path does not need can remain unused.

```csharp
public record Album(int Id, string Title);

Album album = GetAlbumAndArtistName.Query<Album>(cnn);
// Id | Title | ArtistName -> Album uses Id and Title.
```

[Map a nested object](nesting.md).
