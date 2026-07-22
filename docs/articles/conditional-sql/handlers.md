# Handlers

A handler reshapes the SQL with an actual value, not just a present-or-absent key. The syntax is a suffix on the variable. `@Var_X` means variable `@Var` through handler `X`. The suffix is not part of the name, you still supply `@Var`.

## `_X`, collection spreading

Expands an `IEnumerable` into one parameter per item.

```sql
SELECT * FROM tracks WHERE GenreId IN (@GenreIds_X)

-- @GenreIds = [1, 2, 3]
SELECT * FROM tracks WHERE GenreId IN (@GenreIds_1, @GenreIds_2, @GenreIds_3)
```

Combined with `?`, an absent collection drops the whole clause. An empty collection counts as absent, so no `IN ()` is ever produced.

```sql
SELECT * FROM tracks WHERE GenreId IN (?@GenreIds_X)

-- @GenreIds = [] or not supplied
SELECT * FROM tracks
```

## `_N`, number injection

Writes a number into the SQL text.

```sql
SELECT Name FROM products ORDER BY Id OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY

-- @Skip = 50, @Take = 50
SELECT Name FROM products ORDER BY Id OFFSET 50 ROWS FETCH NEXT 50 ROWS ONLY

-- neither
SELECT Name FROM products ORDER BY Id
```

## `_S`, string literal

Writes a string wrapped in single quotes. A quote inside the value is doubled, so the literal carries the whole value and the statement ends where you wrote it.

```sql
SELECT * FROM artists WHERE Name = @Name_s

-- @Name = Queen
SELECT * FROM artists WHERE Name = 'Queen'

-- @Name = O'Brien
SELECT * FROM artists WHERE Name = 'O''Brien'
```

## `_R`, raw SQL

Writes the value verbatim, no escaping.

```sql
SELECT Id, Name FROM @Table_R WHERE IsActive = 1

-- @Table = tracks
SELECT Id, Name FROM tracks WHERE IsActive = 1
```

> **Warning.** `_R` is textual injection. Only pass values you fully control, never user input.

## When `_R` decides the columns

The row parser is cached per set of active keys, and a handler value is not a key.

```sql
SELECT @Cols_R FROM users
```

Run that with `@Cols = "Id, Name"`, then with `@Cols = "Email, City"`, and the second run maps its rows with the first run's parser. Nothing throws, the values land in the wrong places.

Use one command per shape.

```csharp
static readonly QueryCommand Names = new("SELECT Id, Name FROM users");
static readonly QueryCommand Contacts = new("SELECT Email, City FROM users");
```

Or make the columns keys, with [markers](conditional-markers.md) or a [`?SELECT`](dynamic-projection.md). Each combination is its own key set, so it gets its own parser and one command covers them all.

```sql
?SELECT Id&, Name, Email&, City FROM users

-- Name on
SELECT Id, Name FROM users

-- City on
SELECT Email, City FROM users
```

## Errors happen at generation

A handled variable that is required by an active segment but missing raises `RequiredHandlerValueException` while the SQL is being generated, before any database call. The handler needs the value to build the string.

## Registering your own letter

The four built-ins are entries in two global letter maps, and unused letters are yours. There are two kinds:

- A handler that only writes SQL text (like `_N`, `_S`, `_R`) implements `IQuerySegmentHandler` and registers in `QueryFactory.BaseHandlerMapper`.
- A handler that also touches the `DbCommand`, binding parameters (like `_X`), derives from `SpecialHandler` and registers in `SpecialHandler.SpecialHandlerGetter`.

```csharp
// @When_D writes a provider-specific date literal
QueryFactory.BaseHandlerMapper['D'] = _ => new LegacyDateHandler();

// @Secret_P binds an encrypted parameter
SpecialHandler.SpecialHandlerGetter['P'] = name => new EncryptionHandler(name);
```

The factory delegate receives the variable name. Letters are case-insensitive, `A` to `Z`, and the maps are global. Register at startup, before the first `QueryCommand` is built.
