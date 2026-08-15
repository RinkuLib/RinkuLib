# Conditional markers

`/*Key*/` keeps its SQL footprint when the named key is active.

```csharp
static readonly QueryCommand Albums = new("SELECT AlbumId AS Id, Title, /*IncludeYear*/ReleaseYear FROM albums");

var values = Albums.StartBuilder();
values.Use("IncludeYear");

List<DynaObject> albums = values.Query<List<DynaObject>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums
```

Without the key, the column and its comma disappear.

```csharp
List<DynaObject> albums = Albums.Query<List<DynaObject>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

## Activate keys from parameter types

`[ForBoolCond]` uses the member name as a key when its value is true.

```csharp
public sealed class AlbumOptions {
    [ForBoolCond] public bool IncludeYear { get; init; }
}

List<DynaObject> albums = Albums.Query<List<DynaObject>>(cnn, new AlbumOptions { IncludeYear = true });
```

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums
```

A false member value leaves its condition key inactive.

```csharp
List<DynaObject> albums = Albums.Query<List<DynaObject>>(cnn, new AlbumOptions { IncludeYear = false });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

`[UsesBoolConds]` activates its keys whenever the type supplies values.

```csharp
[UsesBoolConds("IncludeYear")]
public sealed class AlbumReportOptions {
    public int? ArtistId { get; init; }
}

List<DynaObject> albums = Albums.Query<List<DynaObject>>(cnn, new AlbumReportOptions());
```

```sql
SELECT AlbumId AS Id, Title, ReleaseYear FROM albums
```

## Tie a marker to a parameter

`/*@artistId*/` uses the presence of `@artistId` as its key.

```csharp
static readonly QueryCommand ArtistAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*@artistId*/ArtistId = @artistId");

List<Album> albums = ArtistAlbums.Query<List<Album>>(cnn);
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

```csharp
List<Album> albums = ArtistAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

For a plain condition, this produces the same SQL as `?@artistId`. The explicit marker has a stricter boundary around parentheses.

## Parentheses bound explicit markers

An explicit marker inside parentheses removes only its term.

```csharp
static readonly QueryCommand SearchInvoices = new("SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min AND (Country = @country OR /*@city*/City = @city)");

List<Invoice> invoices = SearchInvoices.Query<List<Invoice>>(cnn, new { min = 100m, country = "Canada" });
```

```sql
SELECT InvoiceId AS Id, Total FROM invoices WHERE Total > @min AND (Country = @country)
```

The `?@city` form instead grows out of those parentheses and removes the complete parenthesized condition.

## Make a clause conditional

A marker immediately before a section keyword controls that section.

```csharp
static readonly QueryCommand CountryInvoices = new(
    "SELECT i.InvoiceId AS Id FROM invoices i /*@country*/JOIN customers c ON c.CustomerId = i.CustomerId WHERE c.Country = ?@country");

List<Invoice> invoices = CountryInvoices.Query<List<Invoice>>(cnn);
```

```sql
SELECT i.InvoiceId AS Id FROM invoices i
```

```csharp
List<Invoice> invoices = CountryInvoices.Query<List<Invoice>>(cnn, new { country = "Canada" });
```

```sql
SELECT i.InvoiceId AS Id FROM invoices i JOIN customers c ON c.CustomerId = i.CustomerId WHERE c.Country = @country
```

Each section is independent. Mark dependent sections separately.

```csharp
static readonly QueryCommand CustomerCounts = new("SELECT Country, COUNT(*) AS Total FROM customers /*Grouped*/GROUP BY Country /*Grouped*/HAVING COUNT(*) > 1");
```

When `Grouped` is inactive, both marked sections disappear.

```sql
SELECT Country, COUNT(*) AS Total FROM customers
```

Activating `Grouped` keeps both the `GROUP BY` and `HAVING` sections.

```sql
SELECT Country, COUNT(*) AS Total FROM customers GROUP BY Country HAVING COUNT(*) > 1
```

## Require several keys

Adjacent markers on one footprint form an implicit AND.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE /*Cheap*//*Available*/Price < @maximum
```

The condition remains when both keys are active.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Price < @maximum
```

If either key is inactive, the complete condition disappears.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

One marker can combine the keys with `&`.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE /*Cheap&Available*/Price < @maximum
```

## Accept any key

`|` keeps the footprint when either key is active.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE /*Recent|Featured*/ReleaseYear >= @year
```

Activating either `Recent` or `Featured` keeps the condition.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear >= @year
```

Expressions are evaluated from left to right without operator precedence.

```text
/*A|B&C*/ = (A OR B) AND C
```

## Negate a key

`!` keeps the footprint while its key is inactive.

```csharp
static readonly QueryCommand ActiveAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE /*!All*/IsArchived = 0");
```

While `All` is inactive, the condition remains.

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE IsArchived = 0
```

Activating `All` removes the negated condition.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

Write `/*!All*/` without a space after `!`. In `/*! All*/`, the leading space becomes part of the key.

## Merge neighboring footprints

`&AND`, `&OR`, and `&,` make neighboring footprints stay or disappear together.

```sql
SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceDate >= ?@from &AND InvoiceDate < ?@until
```

When only one value is supplied, both neighboring footprints disappear.

```sql
SELECT InvoiceId AS Id, Total FROM invoices
```

Supplying both values keeps the complete date range.

```sql
SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceDate >= @from AND InvoiceDate < @until
```

The comma form groups projected columns under one key.

```sql
SELECT CustomerId AS Id, /*Address*/City&, Street&, PostalCode FROM customers
```

When `Address` is inactive, the grouped columns all disappear.

```sql
SELECT CustomerId AS Id FROM customers
```

Activating `Address` keeps every column in the group.

```sql
SELECT CustomerId AS Id, City, Street, PostalCode FROM customers
```

## Stop a footprint with `???`

`???` emits nothing and prevents a footprint from crossing it.

```sql
SELECT DISTINCT??? /*ShowId*/AlbumId AS Id, Title FROM albums
```

With the boundary in place, an inactive `ShowId` removes only the identifier.

```sql
SELECT DISTINCT Title FROM albums
```

Without the boundary, `DISTINCT` belongs to the conditional footprint.

```sql
SELECT DISTINCT /*ShowId*/AlbumId AS Id, Title FROM albums
```

Without the boundary, an inactive `ShowId` also removes `DISTINCT`.

```sql
SELECT Title FROM albums
```

## Keep a block comment

Prefix a block comment with `~` so it is emitted instead of parsed as a marker.

```sql
/*~ application note */SELECT AlbumId AS Id, Title FROM albums
```

The generated SQL keeps the comment and removes the `~` marker.

```sql
/* application note */SELECT AlbumId AS Id, Title FROM albums
```

Line comments are already treated as literal text.

Continue with [conditional column selection](dynamic-projection.md).
