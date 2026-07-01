# Special clauses

*CASE, JOIN, CTE, and subquery edge cases.*

## Conditional JOIN with combined conditions

A single `/*...*/` before a `JOIN` can respond to several keys. If **any** fire, the join is included.

* **Template:** `SELECT i.InvoiceId, i.Total, /*Name*/c.FirstName FROM invoices i /*@Country|Name*/INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = ?@Country`
* **Result (nothing provided):** `SELECT i.InvoiceId, i.Total FROM invoices i`
* **Result (`@Country` provided):** `SELECT i.InvoiceId, i.Total FROM invoices i INNER JOIN customers c ON i.CustomerId = c.CustomerId WHERE c.Country = @Country`
* **Result (`Name` provided):** `SELECT i.InvoiceId, i.Total, c.FirstName FROM invoices i INNER JOIN customers c ON i.CustomerId = c.CustomerId`

## CASE expressions

In a `CASE`, the keywords `WHEN`, `THEN`, and `ELSE` are **section keywords**. Making only the `WHEN` conditional leaves a dangling `THEN`.

* **Template (incorrect):** `SELECT CASE WHEN Role = ?@SpecialRole THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`
* **Result:** `SELECT CASE THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users` (invalid)

Mark the matching `THEN` with the same key to remove the pair cleanly.

* **Template (correct):** `SELECT CASE Role = ?@SpecialRole /*@SpecialRole*/THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`
* **Result:** `SELECT CASE WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`
