# Conditional markers

`/*...*/` places a condition exactly where you want it. `?@Var` is shorthand for `/*@Var*/@Var` with one difference: the footprint of `?@Var` grows out of ordinary parentheses on its own, while `/*...*/` treats every parenthesis as a boundary, giving you precise control.

## Tied to a variable

`/*@Var*/` makes the segment after it conditional on `@Var` being supplied.

* **Template:** `SELECT * FROM tracks WHERE /*@AlbumId*/AlbumId = (SELECT AlbumId FROM albums WHERE AlbumId = @AlbumId)`
* **Result (no `@AlbumId`):** `SELECT * FROM tracks`

Or keep the condition inside a parenthesized group:

* **Template:** `SELECT * FROM invoices WHERE Total > @Min AND (Country = @C1 OR /*@City*/City = @City)`
* **Result (no `@City`):** `SELECT * FROM invoices WHERE Total > @Min AND (Country = @C1)`

## Custom keys

A comment without a variable defines a key you activate yourself, no value attached.

* **Template:** `SELECT * FROM tracks WHERE GenreId = 1 AND /*HighPriced*/UnitPrice > 1`
* **Result (key not used):** `SELECT * FROM tracks WHERE GenreId = 1`

```csharp
var b = cmd.StartBuilder();
b.Use("HighPriced");                 // from a builder
// or [ForBoolCond] / [UsesBoolConds] on a parameter object
```

Columns work the same way.

* **Template:** `SELECT TrackId, Name, /*ShowPrice*/UnitPrice FROM tracks`
* **Result (key not used):** `SELECT TrackId, Name FROM tracks`

## Whole clauses

Placed before a clause, the marker takes the whole clause as its footprint.

* **Template:** `SELECT i.InvoiceId FROM invoices i /*@Country*/INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country`
* **Result (no `@Country`):** `SELECT i.InvoiceId FROM invoices i`
* **Result (`@Country` supplied):** the join and the filter both stay.

A dynamic `ORDER BY` combines a clause marker with [handlers](handlers.md):

* **Template:** `SELECT * FROM products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R`
* **Result (`@Sort` = `Price`, `@Dir` = `DESC`):** `SELECT * FROM products WHERE IsActive = 1 ORDER BY Price DESC`

## Combining keys: `|`, `&`, `!`

Inside a marker, combine keys with `|` (or), `&` (and), `!` (not). Read left to right, no precedence: `/*A|B&C*/` means `(A or B) and C`. Spaces are allowed.

* **Template:** `SELECT * FROM tracks WHERE /*Cheap|Pricey&InCatalog*/UnitPrice > 1`
* The segment stays when (`Cheap` or `Pricey`) and `InCatalog` are active.

`!` keeps the segment only when the key is absent. The classic use is an "all" switch:

* **Template:** `SELECT * FROM tracks WHERE /*!All*/GenreId = 1`
* **Result (`All` not used):** `SELECT * FROM tracks WHERE GenreId = 1`
* **Result (`All` used):** `SELECT * FROM tracks`

A join that several features depend on listens to all of them:

* **Template:** `SELECT i.InvoiceId, /*Name*/c.FirstName FROM invoices i /*@Country|Name*/JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country`
* Either the `@Country` filter or the `Name` column pulls the join in.

## Grouping with `&AND`, `&OR`, `&,`

Prefix a connector with `&` to weld the segments around it into one unit, kept or dropped together.

* **Template:** `SELECT * FROM invoices WHERE InvoiceDate > ?@MinDate &AND InvoiceDate < ?@MaxDate`
* **Result (only `@MinDate`):** `SELECT * FROM invoices`
* **Result (both):** `SELECT * FROM invoices WHERE InvoiceDate > @MinDate AND InvoiceDate < @MaxDate`

It welds a static condition to an optional one:

* **Template:** `SELECT * FROM customers WHERE Country = 'USA' &OR Country = ?@Country`
* **Result (no `@Country`):** `SELECT * FROM customers`

And it works with commas, in projections and `SET` lists:

* **Template:** `UPDATE customers SET Phone = '000' &, Email = ?@Email, Company = @Company WHERE CustomerId = @Id`
* **Result (no `@Email`):** `UPDATE customers SET Company = @Company WHERE CustomerId = @Id`, the welded `Phone` assignment left with it.

* **Template:** `SELECT Id, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM users`
* **Result (key used):** `SELECT Id, Username, City, Street, ZipCode FROM users`
* **Result (key not used):** `SELECT Id, Username FROM users`

## The `???` boundary

`???` is a wall a footprint cannot cross. It emits nothing.

* **Template:** `SELECT DISTINCT /*ShowId*/TrackId, Name FROM tracks`
* **Result (key not used):** `SELECT Name FROM tracks`, `DISTINCT` was swept into the pruned footprint.

* **Template:** `SELECT DISTINCT ??? /*ShowId*/TrackId, Name FROM tracks`
* **Result (key not used):** `SELECT DISTINCT Name FROM tracks`

The same wall makes a modifier itself conditional:

* **Template:** `SELECT /*UseDistinct*/DISTINCT ??? Id, Name FROM users`
* **Result (key not used):** `SELECT Id, Name FROM users`

## Keeping a real comment

Start a comment with `~` to pass it through instead of parsing it.

* **Template:** `/*~ join hint */SELECT TrackId FROM tracks`
* **Result:** `/* join hint */SELECT TrackId FROM tracks`

## Sections in practice

The engine treats every keyword section identically, and sometimes that uniformity is the thing to know.

**CASE expressions.** `WHEN`, `THEN`, and `ELSE` are ordinary sections, so a footprint stops at them like it stops at a `WHERE`. Making only a `WHEN` conditional strands its `THEN`; mark the `THEN` with the same key so the pair leaves together.

* **Template:** `CASE WHEN Role = ?@Special /*@Special*/THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END`
* **Result (no `@Special`):** `CASE WHEN Role = 'Admin' THEN 'A' ELSE 'U' END`

**INSERT statements.** Pair the column and its value under one key, either with the same variable key on both sides, or `&,` groups on both lists.

* **Template:** `INSERT INTO users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)`
* **Result (no `@Email`):** `INSERT INTO users (Username) VALUES (@Username)`

* **Template:** `INSERT INTO profiles (UserId, /*Details*/Bio&, Website&, AvatarUrl) VALUES (@Uid, /*Details*/@Bio&, @Web&, @Img)`
* Both lists gain or lose their trio together under the `Details` key.

Note: inside an `INSERT`, `?@Var` cannot grow its footprint out of the first-level column and `VALUES` parentheses. Use the paired markers above.
