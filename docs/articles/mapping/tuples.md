# Tuples

Tuple elements read from left to right. The first element claims its columns, then the next element starts from the columns that remain.

## Read scalar columns in order

```csharp
static readonly QueryCommand GetAlbumSummary = new("SELECT AlbumId, Title FROM albums WHERE AlbumId = @albumId");

(int id, string title) = GetAlbumSummary.Query<(int, string)>(cnn, new { albumId = 1 });
```

The `int` reads the first compatible column. The `string` reads the next compatible column.

## Tuple names do not match columns

Tuple element names exist for the C# caller only. They do not change how the result is read.

```csharp
(int number, string text) = GetAlbumSummary.Query<(int DifferentName, string AlsoDifferent)>(cnn, new { albumId = 1 });
```

```text
AlbumId -> number
Title   -> text
```

Use an object when columns should map by name instead of position.

## Keep a value beside an object

A tuple can keep a database value that does not belong on the mapped object.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbumWithArtistId = new("SELECT ArtistId, AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId");

(int artistId, Album album) = GetAlbumWithArtistId.Query<(int, Album)>(cnn, new { albumId = 1 });
```

`ArtistId` fills the first tuple element. `Album` starts afterward and maps `Id` and `Title` by name. The returned `Album` remains independent from the relationship key.

## Read the same type twice

Repeated types are useful when one row contains the same model in two roles.

```csharp
public record Employee(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetEmployeeAndManager = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS Id, m.Name FROM employees e JOIN employees m ON m.EmployeeId = e.ManagerId WHERE e.EmployeeId = @employeeId");

(Employee employee, Employee manager) = GetEmployeeAndManager.Query<(Employee, Employee)>(cnn, new { employeeId = 1 });
```

The first `Employee` claims the first `Id` and `Name`. The second one continues from the next `Id` and `Name`.

## Combine different object shapes

Each object keeps its own mapping rules after the tuple chooses where it starts.

```csharp
public record Order(int Id, decimal Total) : IDbReadable;
public record Customer(int Id, string Name, string? Email = null) : IDbReadable;

static readonly QueryCommand GetOrderWithCustomer = new("SELECT o.OrderId AS Id, o.Total, c.CustomerId AS Id, c.Name, c.Email FROM orders o JOIN customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId = @orderId");

(Order order, Customer customer) = GetOrderWithCustomer.Query<(Order, Customer)>(cnn, new { orderId = 1 });
```

`Order` uses `Id` and `Total`. `Customer` starts after `Total` and uses the remaining columns.

## Use tuples inside a collection

A collection of tuples reads the same sequential shape for every row. This is useful when application code groups rows itself.

```csharp
public record Album(int Id, string Title) : IDbReadable;

static readonly QueryCommand GetAlbumsWithArtistIds = new("SELECT ArtistId, AlbumId AS Id, Title FROM albums ORDER BY ArtistId, AlbumId");

List<(int ArtistId, Album Album)> rows = GetAlbumsWithArtistIds.Query<List<(int, Album)>>(cnn);
```

```csharp
foreach ((int artistId, Album album) in rows)
    Console.WriteLine($"{artistId}: {album.Title}");
```

## Large tuples

Tuples with more than seven elements use nested `ValueTuple` storage internally, but Rinku continues reading their logical elements in order.

```csharp
var values = GetStatistics.Query<(int, int, int, int, int, int, int, int)>(cnn);
```

Normal C# deconstruction and `Item1`, `Item2`, and later properties work as expected.

## Missing elements

Every tuple element must find a readable value. A missing element makes the tuple path unusable.

```csharp
static readonly QueryCommand GetOnlyId = new("SELECT AlbumId FROM albums WHERE AlbumId = @albumId");

var result = GetOnlyId.Query<(int, string)>(cnn, new { albumId = 1 });
// RINKU3001 because no column can fill the string.
```

An incompatible column type produces the same mapping error.

```csharp
var result = GetAlbumSummary.Query<(int, Stream)>(cnn, new { albumId = 1 });
// RINKU3001 because Title cannot be mapped to Stream.
```

The tuple itself follows the requested result shape. An unwrapped tuple requires one result, while `List<(int, string)>` buffers every row and `OptionalStruct<(int, string)>` permits no row.

[Reading order](reading-order.md) covers gaps, column reuse, and sequential attributes. [Result shapes](../running-queries/result-shapes.md) covers row counts and buffering.
