# Conditional variables

Add `?` before a variable when its SQL should remain only when a value is supplied.

```csharp
static readonly QueryCommand Albums = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = ?@albumId");
```

Without the value, the condition and empty `WHERE` clause are removed.

```csharp
List<Album> albums = Albums.Query<List<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

Supplying the value keeps the condition.

```csharp
Album album = Albums.Query<Album>(cnn, new { albumId = 1 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId
```

A `null` member is also absent.

```csharp
int? albumId = null;
List<Album> albums = Albums.Query<List<Album>>(cnn, new { albumId });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

The same marker works outside a `WHERE` clause.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = ?@title, ReleaseYear = ?@releaseYear WHERE AlbumId = @albumId");

int affected = UpdateAlbum.Execute(cnn, new { albumId = 1, title = "New title" });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
```

Supplying both values keeps both assignments.

```csharp
int affected = UpdateAlbum.Execute(cnn, new { albumId = 1, title = "New title", releaseYear = 2026 });
```

```sql
UPDATE albums SET Title = @title, ReleaseYear = @releaseYear WHERE AlbumId = @albumId
```

## The surrounding expression is optional

The removable footprint includes the expression around the variable.

```csharp
static readonly QueryCommand SearchByTitle = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title LIKE CONCAT('%', ?@title, '%')");

List<Album> albums = SearchByTitle.Query<List<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

Supplying the value keeps the complete expression.

```csharp
List<Album> albums = SearchByTitle.Query<List<Album>>(cnn, new { title = "Blue" });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title LIKE CONCAT('%', @title, '%')
```

## Required values inside an optional footprint

A plain variable is required only when its surviving SQL uses it.

```csharp
static readonly QueryCommand SearchByTotal = new("SELECT InvoiceId AS Id, Total FROM invoices WHERE Total BETWEEN @minimum AND ?@maximum");

List<Invoice> invoices = SearchByTotal.Query<List<Invoice>>(cnn);
```

```sql
SELECT InvoiceId AS Id, Total FROM invoices
```

Supplying `maximum` keeps the footprint, so `minimum` is then required.

```csharp
List<Invoice> invoices = SearchByTotal.Query<List<Invoice>>(cnn, new { minimum = 10m, maximum = 100m });
```

```sql
SELECT InvoiceId AS Id, Total FROM invoices WHERE Total BETWEEN @minimum AND @maximum
```

## Parenthesized expressions

An optional variable grows out of ordinary parentheses containing its expression.

```csharp
static readonly QueryCommand SearchInvoices = new("SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min AND (Country = @country OR City = ?@city)");

List<Invoice> invoices = SearchInvoices.Query<List<Invoice>>(cnn, new { min = 100m });
```

```sql
SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min
```

Use an [explicit marker](markers.md#parentheses-bound-explicit-markers) when only one term inside the parentheses should disappear.

## Send database NULL

`null` means absent. Use `DBNull.Value` when the parameter must be present with a database `NULL` value.

```csharp
static readonly QueryCommand ClearTitle = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");

ClearTitle.Execute(cnn, new { albumId = 1, title = DBNull.Value });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
-- @title contains database NULL.
```

`[UseDbNull]` keeps nullable parameter models strongly typed.

```csharp
public sealed class AlbumUpdate {
    public int AlbumId { get; init; }
    [UseDbNull] public string? Title { get; init; }
}

ClearTitle.Execute(cnn, new AlbumUpdate { AlbumId = 1, Title = null });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
-- @title contains database NULL.
```

[Expand a collection into parameters](collections.md).
