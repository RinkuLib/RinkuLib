# Conditional markers

`/*...*/` places a condition in the SQL. Its key is either a variable, active when the variable is supplied, or a name you choose, active when you turn it on.

## `?@Var` written out

`?@Var` places a marker on its own variable. On a plain condition, the two forms are the same query.

```sql
SELECT * FROM tracks WHERE Name = ?@Name
SELECT * FROM tracks WHERE Name = /*@Name*/@Name

-- no @Name, either form
SELECT * FROM tracks
```

They differ at parentheses: the footprint of `?` [grows out of them](optional-variables.md#parentheses-and-subqueries), a marker's never does.

## Custom keys

A marker does not need a variable: `/*Key*/` prunes by a key you activate yourself, no value attached.

```sql
SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId AND /*HasComposer*/Composer IS NOT NULL

-- HasComposer off
SELECT TrackId, Name FROM tracks WHERE AlbumId = @albumId
```

```csharp
var b = cmd.StartBuilder();
b.Use("HasComposer");                // from a builder
// or [ForBoolCond] / [UsesBoolConds] on a parameter object
```

Columns work the same way.

```sql
SELECT TrackId, Name, /*ShowPrice*/UnitPrice FROM tracks

-- ShowPrice off
SELECT TrackId, Name FROM tracks
```

## The footprint

A footprint runs between structural boundaries: a connector (`AND`, `OR`), a list comma, a section keyword (`WHERE`, `SET`, `THEN`, any of them), a parenthesis. The marker can sit anywhere between those boundaries, so these three are the same query.

```sql
SELECT TrackId, Name FROM tracks WHERE /*Long*/Milliseconds > @ms
SELECT TrackId, Name FROM tracks WHERE Milliseconds /*Long*/> @ms
SELECT TrackId, Name FROM tracks WHERE Milliseconds > /*Long*/@ms

-- Long off, any form
SELECT TrackId, Name FROM tracks

-- Long on, any form
SELECT TrackId, Name FROM tracks WHERE Milliseconds > @ms
```

Space around the marker is cosmetic. It can sit on either side or both and drops with the marker. The footprint's edges are the boundary tokens, not the spaces. The one rule is not to weld two words. Write `WHERE /*Long*/Milliseconds` or `WHERE/*Long*/ Milliseconds`, never `WHERE/*Long*/Milliseconds`.

The connector after a footprint belongs to it, so a marker just before an `AND`, `OR`, or comma binds the condition on its left, not the one on its right.

```sql
SELECT * FROM tracks WHERE Composer = @composer /*Extra*/AND Milliseconds > @ms

-- Extra off, the left condition and its AND prune
SELECT * FROM tracks WHERE Milliseconds > @ms
```

Move the marker past the connector to bind the condition on the right.

An expression is one footprint however deep it goes. A subquery inside it comes along, even when the variable sits in the subquery.

```sql
SELECT * FROM tracks WHERE /*@AlbumId*/AlbumId = (SELECT AlbumId FROM albums WHERE AlbumId = @AlbumId)

-- no @AlbumId
SELECT * FROM tracks
```

A parenthesis bounds a marker's footprint, so inside a group it prunes only its term. `?@Var` is the one exception: its footprint [grows out of plain parentheses](optional-variables.md#parentheses-and-subqueries).

```sql
SELECT * FROM invoices WHERE Total > @MinTotal AND (Country = @Country OR /*@City*/City = @City)

-- no @City
SELECT * FROM invoices WHERE Total > @MinTotal AND (Country = @Country)
```

## Making a whole clause conditional

The footprints above are conditions. A marker placed right before a section keyword instead takes the whole clause it introduces, from the keyword to the next section or the statement's end. This is the way to make a join, an `ORDER BY`, or any clause come and go.

Here the join appears only when the filter that needs it does, both keyed on `@Country`.

```sql
SELECT i.InvoiceId FROM invoices i /*@Country*/INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country

-- no @Country
SELECT i.InvoiceId FROM invoices i

-- @Country supplied
SELECT i.InvoiceId FROM invoices i INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = @Country
```

Keep a space before the marker, `i /*@Country*/INNER JOIN`, so the clause in front of it is not pulled in.

It reaches exactly one clause, never the one that follows. Dropping a `GROUP BY` leaves its `HAVING` stranded, which is invalid.

```sql
SELECT Country, COUNT(*) FROM customers /*Grouped*/GROUP BY Country HAVING COUNT(*) > 1

-- Grouped off, the HAVING is stranded
SELECT Country, COUNT(*) FROM customers HAVING COUNT(*) > 1
```

Mark the dependent clause with the same key so the two leave together. Likewise, each join in a chain needs its own marker.

```sql
SELECT Country, COUNT(*) FROM customers /*Grouped*/GROUP BY Country /*Grouped*/HAVING COUNT(*) > 1

-- Grouped off
SELECT Country, COUNT(*) FROM customers
```

## Several conditions, one footprint

Every condition touching a footprint must be active for it to stay, an implicit "and".

```sql
SELECT * FROM tracks WHERE /*Cheap*//*InCatalog*/UnitPrice > @minPrice

-- Cheap and InCatalog on
SELECT * FROM tracks WHERE UnitPrice > @minPrice

-- Cheap on, InCatalog off
SELECT * FROM tracks
```

`?@Var` counts too. This footprint needs `Premium` on and `@minPrice` supplied, just as [several `?@Var` in one footprint](optional-variables.md#how-far-the-footprint-reaches) need every value.

```sql
SELECT * FROM tracks WHERE /*Premium*/UnitPrice > ?@minPrice

-- Premium off, or no @minPrice
SELECT * FROM tracks

-- Premium on with @minPrice
SELECT * FROM tracks WHERE UnitPrice > @minPrice
```

## Combining keys: `|` and `&`

Inside one marker, `&` writes that same "and", and `|` brings "or".

```sql
SELECT * FROM tracks WHERE /*Cheap*//*InCatalog*/UnitPrice > @minPrice
SELECT * FROM tracks WHERE /*Cheap&InCatalog*/UnitPrice > @minPrice

-- Cheap and InCatalog on, either form
SELECT * FROM tracks WHERE UnitPrice > @minPrice
```

Keys read left to right, no precedence: `/*A|B&C*/` is `(A or B) and C`. Spaces around keys and operators are ignored, so these two are the same marker.

```sql
SELECT * FROM tracks WHERE /*Cheap|Pricey&InCatalog*/UnitPrice > @minPrice
SELECT * FROM tracks WHERE /* Cheap | Pricey & InCatalog */UnitPrice > @minPrice

-- Cheap and InCatalog on, either form
SELECT * FROM tracks WHERE UnitPrice > @minPrice

-- Cheap on, InCatalog off, either form
SELECT * FROM tracks
```

`|` lets a footprint serve several features. Either the `@Country` filter or the `Name` column pulls the join in.

```sql
SELECT i.InvoiceId, /*Name*/c.FirstName FROM invoices i /*@Country|Name*/JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country

-- Name on, no @Country
SELECT i.InvoiceId, c.FirstName FROM invoices i JOIN customers c ON i.CustomerId = c.CustomerId

-- neither
SELECT i.InvoiceId FROM invoices i
```

## Negating a key: `!`

`!` in front of a key flips it, so the footprint stays only when that key is absent. The classic use is an "all" switch that drops a fixed filter, one with no variable you could simply leave unsupplied.

```sql
SELECT * FROM products WHERE /*!All*/IsActive = 1

-- All off
SELECT * FROM products WHERE IsActive = 1

-- All on
SELECT * FROM products
```

Unlike the combine operators, `!` must touch its key. `/*! All*/` with a space does not negate.

## Merging footprints: `&AND`, `&OR`, `&,`

Prefix a connector with `&` and the footprints on both sides become one, kept or dropped together.

```sql
SELECT * FROM invoices WHERE InvoiceDate > ?@MinDate &AND InvoiceDate < ?@MaxDate

-- only @MinDate
SELECT * FROM invoices

-- both supplied
SELECT * FROM invoices WHERE InvoiceDate > @MinDate AND InvoiceDate < @MaxDate
```

It merges a static condition into an optional one.

```sql
SELECT * FROM customers WHERE Country = 'USA' &OR Country = ?@Country

-- no @Country
SELECT * FROM customers
```

And it works with commas, in projections and `SET` lists. `Phone` and `Email` update together or neither.

```sql
UPDATE customers SET Phone = @phone &, Email = ?@Email, Company = @Company WHERE CustomerId = @Id

-- no @Email
UPDATE customers SET Company = @Company WHERE CustomerId = @Id
```

```sql
SELECT Id, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM users

-- IncludeAddress on
SELECT Id, Username, City, Street, ZipCode FROM users

-- IncludeAddress off
SELECT Id, Username FROM users
```

## The `???` boundary

`???` is a wall a footprint cannot cross. It emits nothing.

Without it, `DISTINCT` is swept into the pruned footprint.

```sql
SELECT DISTINCT /*ShowId*/TrackId, Name FROM tracks

-- ShowId off
SELECT Name FROM tracks
```

With the wall in front of the marker, `DISTINCT` stays put.

```sql
SELECT DISTINCT ??? /*ShowId*/TrackId, Name FROM tracks

-- ShowId off
SELECT DISTINCT Name FROM tracks
```

The same wall makes a modifier itself conditional.

```sql
SELECT /*UseDistinct*/DISTINCT ??? Id, Name FROM users

-- UseDistinct off
SELECT Id, Name FROM users
```

## Keeping a real comment

Start a comment with `~` to pass it through instead of parsing it.

```sql
/*~ join hint */SELECT TrackId FROM tracks

-- result
/* join hint */SELECT TrackId FROM tracks
```

## In practice

A dynamic `ORDER BY` comes from a clause marker and two [handlers](handlers.md).

```sql
SELECT * FROM products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R

-- @Sort = Price, @Dir = DESC
SELECT * FROM products WHERE IsActive = 1 ORDER BY Price DESC
```

`WHEN`, `THEN`, and `ELSE` are ordinary sections, so a footprint stops at them like it stops at a `WHERE`. Making only a `WHEN` conditional strands its `THEN`. Mark the `THEN` with the same key and the pair leaves together.

```sql
CASE WHEN Role = ?@Special /*@Special*/THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END

-- no @Special
CASE WHEN Role = 'Admin' THEN 'A' ELSE 'U' END
```

In an `INSERT`, pair a column with its value under one key: the same variable key on both sides, or `&,` groups on both lists.

```sql
INSERT INTO users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)

-- no @Email
INSERT INTO users (Username) VALUES (@Username)
```

The `&,` groups weld a trio on each side, so key one member and all three move together. Here `@Bio` drives both columns and values.

```sql
INSERT INTO profiles (UserId, /*@Bio*/Bio&, Website&, AvatarUrl)
VALUES (@Uid, ?@Bio&, @Web&, @Img)

-- no @Bio
INSERT INTO profiles (UserId) VALUES (@Uid)
```

The pairing is needed because `?@Var` cannot grow out of the first-level column and `VALUES` parentheses: on its own it would drop a value but leave the column, and the lists would no longer line up.
