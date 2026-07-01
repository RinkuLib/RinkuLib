# Operators & grouping

*Combine conditions into a single unit.*

## Grouped connectors (`&`)

Prefix a logical connector with `&` to bind conditions so they're kept or dropped together.

* **Template:** `SELECT * FROM invoices WHERE InvoiceDate > ?@MinDate &AND InvoiceDate < ?@MaxDate`
* **Result (only @MinDate provided):** `SELECT * FROM invoices`
* **Result (both provided):** `SELECT * FROM invoices WHERE InvoiceDate > @MinDate AND InvoiceDate < @MaxDate`

It works with `OR`, and can combine an optional condition with a static one.

* **Template:** `SELECT * FROM customers WHERE Country = 'USA' &OR Country = ?@Country`
* **Result (@Country not provided):** `SELECT * FROM customers`
* **Result (@Country provided):** `SELECT * FROM customers WHERE Country = 'USA' OR Country = @Country`

And with commas.

* **Template:** `UPDATE customers SET Phone = '000' &, Email = ?@Email, Company = @Company WHERE CustomerId = @ID`
* **Result:** `UPDATE customers SET Company = @Company WHERE CustomerId = @ID`

## Operators inside a `/*...*/` marker

Inside a comment marker you can combine condition keys with `|` (OR), `&` (AND), and `!` (NOT). The comment is read **left to right, with no precedence**, so `/*A|B&C*/` means `(A OR B) AND C`.

* **Template:** `SELECT * FROM tracks WHERE /*Cheap|Pricey&InCatalog*/ UnitPrice > 1`
* **Result (none of the keys provided):** `SELECT * FROM tracks`

The `!` operator requires the **absence** of a key.

* **Template:** `SELECT * FROM tracks WHERE /*!All*/ GenreId = 1`
* **Result (if `All` is not provided):** `SELECT * FROM tracks WHERE GenreId = 1`
* **Result (if `All` is provided):** `SELECT * FROM tracks`
