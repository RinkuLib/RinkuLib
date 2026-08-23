# Tuple mapping

## Sequential columns

```csharp
(int id, string title) = cnn.Query<(int, string)>("SELECT AlbumId, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

Tuple slots read sequentially. C# tuple element names do not change database column matching.

## Scalar beside a mapped type

```csharp
public record Album(int Id, string Title) : IDbReadable;

(int artistId, Album album) = cnn.Query<(int, Album)>("SELECT ArtistId, AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId", new { albumId = 12 });
```

The scalar consumes the first column. `Album` maps from the remaining columns.

## Same mapped type twice

```csharp
public record Employee(int Id, string Name) : IDbReadable;

(Employee employee, Employee manager) = cnn.Query<(Employee, Employee)>("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS Id, m.Name FROM employees e JOIN employees m ON m.EmployeeId = e.ManagerId WHERE e.EmployeeId = @employeeId", new { employeeId = 12 });
```

The first `Employee` claims the first matching pair. The second continues from the next unused columns.

## Tuple inside a multi-row result

```csharp
public record Album(int Id, string Title) : IDbReadable;

List<(int ArtistId, Album Album)> rows = cnn.Query<List<(int, Album)>>("SELECT ArtistId, AlbumId AS Id, Title FROM albums ORDER BY ArtistId, AlbumId");
```

The collection repeats the same tuple mapping for each element.

[Reading order](reading-order.md) · [Multi-row mapping](collections.md)
