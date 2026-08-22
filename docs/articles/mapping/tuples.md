# Tuples

Tuple elements read from left to right.

```csharp
static readonly QueryCommand GetAlbumSummary = new("SELECT AlbumId, Title FROM albums WHERE AlbumId = @albumId");

(int id, string title) = GetAlbumSummary.Query<(int, string)>(cnn, new { albumId = 1 });
```

The first element reads the first compatible column. The next element continues after it.

## Tuple names do not map columns

```csharp
(int number, string text) = GetAlbumSummary.Query<(int DifferentName, string AlsoDifferent)>(cnn, new { albumId = 1 });
```

Tuple element names are for the C# caller. They do not change database column matching.

Use an object when the result should map by names.

## Keep a value beside an object

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbumWithArtistId = new("SELECT ArtistId, AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

(int artistId, Album album) = GetAlbumWithArtistId.Query<(int, Album)>(cnn, new { albumId = 1 });
```

The scalar value is read first. `Album` maps the remaining columns by name.

## Read the same type twice

```csharp
public record Employee(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetEmployeeAndManager = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS Id, m.Name FROM employees e JOIN employees m ON m.EmployeeId = e.ManagerId WHERE e.EmployeeId = @employeeId");

(Employee employee, Employee manager) = GetEmployeeAndManager.Query<(Employee, Employee)>(cnn, new { employeeId = 1 });
```

The first `Employee` claims the first `Id` and `Name`. The second one continues from the next pair.

## Tuples in collections

```csharp
List<(int ArtistId, Album Album)> rows = GetAlbumsWithArtistIds.Query<List<(int, Album)>>(cnn);
```

A collection of tuples repeats the same sequential shape for each row.

See [reading order](reading-order.md) for attributes that change sequential slot behavior.
