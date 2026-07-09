# Optional variables

`?@Var` marks a variable optional. Supply it and its footprint, the SQL around it, stays. Leave it out and the footprint is pruned, and the statement is still valid.

Write the SQL as if every parameter will be used, then add `?` to the optional ones.

```sql
SELECT * FROM tracks WHERE AlbumId = @albumId AND Name = ?@Name

-- no @Name
SELECT * FROM tracks WHERE AlbumId = @albumId
```

## Automatic cleanup

Pruning removes whatever would dangle: a trailing operator, a comma, an emptied clause keyword.

```sql
UPDATE customers SET Email = @Email, Phone = ?@Phone WHERE CustomerId = @Id

-- no @Phone
UPDATE customers SET Email = @Email WHERE CustomerId = @Id
```

```sql
SELECT * FROM artists WHERE Name = ?@Name ORDER BY Name

-- no @Name
SELECT * FROM artists ORDER BY Name
```

An emptied clause disappears entirely.

```sql
SELECT BillingCountry FROM invoices
GROUP BY BillingCountry HAVING SUM(Total) > ?@MinTotal AND COUNT(*) > ?@MinCount

-- neither supplied
SELECT BillingCountry FROM invoices GROUP BY BillingCountry
```

These are one set of rules, not per-clause behavior. Every keyword section (`SET`, `HAVING`, `ORDER BY`, `THEN`, all of them) cleans up the same way.

## How far the footprint reaches

The footprint is the whole condition around the variable, however big the expression gets. To make that hold, the footprint grows out of any parentheses inside the expression: a function call does not cut it short.

```sql
SELECT Id, Name FROM users WHERE Name LIKE CONCAT('%', ?@Name, '%')

-- no @Name
SELECT Id, Name FROM users
```

It owns the connector after it, not the one before.

```sql
SELECT * FROM customers WHERE City = ?@City OR State = ?@State AND Country = ?@Country

-- no @State
SELECT * FROM customers WHERE City = @City OR Country = @Country
```

The `AND` after `@State` was pruned with it. The `OR` before it stayed.

Several optional variables in one footprint must all be supplied for it to stay, an implicit "and" detailed in [conditional markers](conditional-markers.md#several-conditions-one-footprint).

```sql
SELECT * FROM products WHERE Price * ?@Modifier > ?@Minimum

-- only @Modifier
SELECT * FROM products
```

A required `@` inside an optional footprint does not control it, but must have a value when the footprint is kept, or execution fails.

```sql
SELECT * FROM customers WHERE FullName = @First + ' ' + ?@Last

-- no @Last, the footprint prunes and @First is never needed
SELECT * FROM customers

-- @Last supplied, the footprint stays, so @First must be supplied too
SELECT * FROM customers WHERE FullName = @First + ' ' + @Last
```

## Parentheses and subqueries

The growth does not tell a function's parentheses from a grouping's. A parenthesized group around the variable goes as a whole.

```sql
SELECT * FROM invoices WHERE Total > @MinTotal AND (Country = @Country OR City = ?@City)

-- no @City
SELECT * FROM invoices WHERE Total > @MinTotal
```

To prune one term inside a group and keep the rest, place a [marker](conditional-markers.md#the-footprint) yourself: a marker never grows.

A subquery holds the footprint in. Only the inner part is pruned.

```sql
SELECT * FROM tracks WHERE TrackId IN (SELECT TrackId FROM playlist_track WHERE PlaylistId = ?@PlaylistId)

-- no @PlaylistId
SELECT * FROM tracks WHERE TrackId IN (SELECT TrackId FROM playlist_track)
```

## A layout tip

Internally, markers split the template into segments, and static text after an optional condition becomes its own segment. Put the optionals last and the statics stay in one.

```sql
-- two segments
SELECT * FROM Users WHERE ParentID = @ParentID AND Cat = ?@Cat

-- three segments, same output
SELECT * FROM Users WHERE Cat = ?@Cat AND ParentID = @ParentID
```

Fewer segments, a little less memory and work at run time.
