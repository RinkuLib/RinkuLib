# Optional variables (`?@Var`)

*Mark a variable optional and let the engine prune its footprint.*

The `?` prefix marks a variable as optional. When you use it (for example `?@Var`), the engine works out the **footprint**, the surrounding segment, and treats it as conditional, using the variable name (`@Var`) as the condition key.

You don't need tricks like `WHERE 1=1`. Write standard, valid SQL as if every parameter will be used, then add `?` to make a parameter (and its footprint) conditional.

## Operator association

Logical operators (`AND`, `OR`) belong to the **preceding** variable.

> Template `WHERE col1 = ?@Col1 OR col2 = ?@Col2 AND col3 = ?@Col3` with `@Col2` **not used**.
> - **Correct:** `WHERE col1 = @Col1 OR col3 = @Col3` (the `AND` after `@Col2` was pruned).
> - **Wrong:** `WHERE col1 = @Col1 AND col3 = @Col3` (would only happen if the operator belonged to the *following* variable).

## Automatic cleanup

When an optional variable is unused, the engine removes the trailing operator, dangling commas, and empty clause keywords to keep valid SQL.

* **Template:** `SELECT * FROM tracks WHERE GenreId = 1 AND Name = ?@Name`
* **Result:** `SELECT * FROM tracks WHERE GenreId = 1`

* **Template:** `UPDATE customers SET Email = @Email, Phone = ?@Phone WHERE CustomerId = @ID`
* **Result:** `UPDATE customers SET Email = @Email WHERE CustomerId = @ID`

* **Template:** `SELECT * FROM artists WHERE Name = ?@Name ORDER BY Name`
* **Result:** `SELECT * FROM artists ORDER BY Name`

An empty clause keyword is removed entirely.

* **Template:** `SELECT BillingCountry FROM invoices GROUP BY BillingCountry HAVING SUM(Total) > ?@MinTotal AND COUNT(*) > ?@MinCount`
* **Result (neither provided):** `SELECT BillingCountry FROM invoices GROUP BY BillingCountry`

## Keep non-conditional parts together

Keeping non-conditional parts together lets the engine treat them as one block.

- **Preferred:** `... WHERE GenreId = 1 AND Name = ?@Name` (2 segments)
- **Less optimal:** `... WHERE Name = ?@Name AND GenreId = 1` (3 segments)

## Works anywhere

Conditionals work in CTEs, subqueries, JOINs, and nested contexts.

* **Template:** `SELECT * FROM invoices i JOIN customers c ON i.CustomerId = c.CustomerId AND c.Country = ?@Country`
* **Result:** `SELECT * FROM invoices i JOIN customers c ON i.CustomerId = c.CustomerId`

## All-or-nothing segments

When several optional variables share one segment, they're kept or dropped together.

* **Template:** `SELECT * FROM customers WHERE FullName = ?@FirstName + ' ' + ?@LastName`
* **Result (only @FirstName provided):** `SELECT * FROM customers`

When a segment mixes a required (`@`) and an optional (`?@`) variable, the optional one controls the whole segment, but the required one must still be supplied if the segment is kept.

* **Template:** `SELECT * FROM customers WHERE FullName = @FirstName + ' ' + ?@LastName`
* **If `@LastName` missing:** `SELECT * FROM customers`
* **If `@LastName` provided:** segment kept, and `@FirstName` must also be supplied or execution fails.
