# Optional variables

`?@Var` marks a variable optional. When it is supplied, the SQL keeps its footprint, the surrounding segment. When it is not, the footprint is pruned and the statement stays valid.

Write the SQL as if every parameter will be used, then add `?` to the optional ones.

* **Template:** `SELECT * FROM tracks WHERE GenreId = 1 AND Name = ?@Name`
* **Result (no `@Name`):** `SELECT * FROM tracks WHERE GenreId = 1`

## Automatic cleanup

Pruning removes what would dangle: the trailing operator, commas, and emptied clause keywords.

* **Template:** `UPDATE customers SET Email = @Email, Phone = ?@Phone WHERE CustomerId = @Id`
* **Result:** `UPDATE customers SET Email = @Email WHERE CustomerId = @Id`

* **Template:** `SELECT * FROM artists WHERE Name = ?@Name ORDER BY Name`
* **Result:** `SELECT * FROM artists ORDER BY Name`

An emptied clause disappears entirely.

* **Template:** `SELECT BillingCountry FROM invoices GROUP BY BillingCountry HAVING SUM(Total) > ?@MinTotal AND COUNT(*) > ?@MinCount`
* **Result (neither supplied):** `SELECT BillingCountry FROM invoices GROUP BY BillingCountry`

These are one set of rules, not per-clause behaviors. Every keyword section (`SET`, `HAVING`, `ORDER BY`, `THEN`, all of them) cleans up the same way.

## Operators belong to the variable before them

* **Template:** `WHERE col1 = ?@Col1 OR col2 = ?@Col2 AND col3 = ?@Col3`
* **Result (no `@Col2`):** `WHERE col1 = @Col1 OR col3 = @Col3`

The `AND` after `@Col2` was pruned with it, the `OR` before it stayed.

## The footprint is the whole expression

A variable inside a function call or arithmetic carries the full expression with it.

* **Template:** `SELECT Id, Name FROM users WHERE Name LIKE CONCAT('%', ?@Name, '%')`
* **Result (no `@Name`):** `SELECT Id, Name FROM users`

Several optional variables in one segment share its fate. All must be supplied for the segment to stay.

* **Template:** `SELECT * FROM products WHERE Price * ?@Modifier > ?@Minimum`
* **Result (only `@Modifier`):** `SELECT * FROM products`

A required `@` inside an optional segment does not control the segment, but must be supplied when the segment is kept, or execution fails.

* **Template:** `WHERE FullName = @First + ' ' + ?@Last`
* **No `@Last`:** the clause is pruned. **`@Last` supplied:** the clause stays and `@First` becomes mandatory.

## Works anywhere

There is no list of supported places. The engine tracks sections and nesting depth, not meaning, so everything above holds in CTEs, subqueries, JOIN conditions, CASE branches, at any depth.

* **Template:** `SELECT * FROM invoices i JOIN customers c ON i.CustomerId = c.CustomerId AND c.Country = ?@Country`
* **Result (no `@Country`):** `SELECT * FROM invoices i JOIN customers c ON i.CustomerId = c.CustomerId`

* **Template:** `WITH t AS (SELECT a, b FROM x WHERE cond = ?@inner) SELECT * FROM t`
* **Result (no `@inner`):** `WITH t AS (SELECT a, b FROM x) SELECT * FROM t`

## A small layout tip

Segments form around the markers. Putting the static parts first keeps them in one block.

- Preferred: `WHERE GenreId = 1 AND Name = ?@Name`
- Works, one segment more: `WHERE Name = ?@Name AND GenreId = 1`
