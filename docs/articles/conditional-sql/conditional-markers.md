# Conditional markers (`/*...*/`)

*Place a conditional exactly where you want it.*

The `/*...*/` markers are the core way to define conditional footprints. `?@Var` is shorthand for `/*@Var*/@Var`, with one difference. The footprint of `?@Var` grows outside non-subquery parentheses on its own, while `/*...*/` treats every parenthesis like a subquery parenthesis, giving you precise control over where the footprint sits.

## Controlling parenthesis level

A conditional inside a subquery can control a segment outside it.

* **Template:** `SELECT * FROM tracks WHERE /*@AlbumId*/AlbumId = (SELECT AlbumId FROM albums WHERE AlbumId = @AlbumId)`
* **Result:** `SELECT * FROM tracks`

Or it can stay inside non-subquery parentheses.

* **Template:** `SELECT * FROM invoices WHERE Total > @Min AND (BillingCountry = @C1 OR /*@City*/BillingCity = @City)`
* **Result:** `SELECT * FROM invoices WHERE Total > @Min AND (BillingCountry = @C1)`

> In an `INSERT`, the first-level parentheses of the column list and `VALUES` list cannot be grown into by `?@Var`.

## Custom keys (no variable)

A comment **with** a variable (`/*@Name*/`) makes the segment conditional and checks the variable exists. A comment **without** a variable marks the segment conditional under a custom key you control directly, no value required.

* **Template:** `SELECT * FROM tracks WHERE GenreId = 1 AND /*HighPriced*/ UnitPrice > 1`
* **Result:** `SELECT * FROM tracks WHERE GenreId = 1`

This works for columns too.

* **Template:** `SELECT TrackId, Name, /*ShowPrice*/ UnitPrice FROM tracks`
* **Result:** `SELECT TrackId, Name FROM tracks`

## The `???` forced boundary

`???` is a logical separator that conditionals cannot cross, and it adds nothing to the final SQL. Use it to keep a modifier like `DISTINCT` away from a conditional first column.

* **Template:** `SELECT DISTINCT /*ShowId*/ TrackId, Name FROM tracks`
* **Result:** `SELECT Name FROM tracks` (DISTINCT got pruned with the column)

* **Template:** `SELECT DISTINCT ??? /*ShowId*/ TrackId, Name FROM tracks`
* **Result:** `SELECT DISTINCT Name FROM tracks`

## Keeping a literal comment

Start a comment with `~` to keep it in the output instead of treating it as a marker.

* **Template:** `/*~ join hint */SELECT TrackId, Name FROM tracks`
* **Result:** `/* join hint */ SELECT TrackId, Name FROM tracks`

## Conditional clauses

Placed directly before a clause (such as a `JOIN`), `/*...*/` makes the whole clause conditional.

* **Template:** `SELECT i.InvoiceId FROM invoices i /*FilterByCountry*/ JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country`
* **Result:** `SELECT i.InvoiceId FROM invoices i`

See [special clauses](special-clauses.md) for combined conditions and `CASE`.
