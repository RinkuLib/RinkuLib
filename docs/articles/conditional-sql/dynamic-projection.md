# Dynamic projection

Prefixing a `SELECT` with `?` turns each projected column into its own condition, keyed by the column name or alias. It affects only that `SELECT`'s column list.

It puts a marker on each column for you, so these two templates behave identically.

```sql
?SELECT AlbumId AS Id, Title FROM albums
SELECT /*Id*/AlbumId AS Id, /*Title*/Title FROM albums

-- Title on, either form
SELECT Title FROM albums
```

Activate the keys like any condition key: `builder.Use("Title")`, or a parameter object with `[ForBoolCond]` members.

## Always-kept columns

A `!` right after the column expression keeps it out of the conditional logic. It is always projected.

```sql
?SELECT AlbumId AS Id!, Title, ArtistId FROM albums

-- nothing on
SELECT AlbumId AS Id FROM albums

-- Title on
SELECT AlbumId AS Id, Title FROM albums
```

`!` is only valid inside a `?SELECT`. Anywhere else the template throws at construction.

## Joining columns

`&,` welds columns under one key, the last column's name.

```sql
?SELECT ArtistId AS Id&, Name FROM artists

-- Name on
SELECT ArtistId AS Id, Name FROM artists
```

`Id` is not a key of its own here, it rides with `Name`.

## Adding a key to a column

A marker before a column adds its keys on top of the column's own, combined with "and". Here `Email` needs both its own key and `Admin`.

```sql
?SELECT Id, Username, /*Admin*/Email FROM users

-- Id, Username, Email on; Admin off
SELECT Id, Username FROM users
```

A marker on an always-kept column replaces "always" with the marker's keys, so `Id` is projected when `Manual` is active instead of always.

```sql
?SELECT /*Manual*/Id!, Username FROM users

-- Username on, Manual off
SELECT Username FROM users

-- Username and Manual on
SELECT Id, Username FROM users
```

## Under an outer condition

A `?SELECT` can itself sit inside a bigger conditional footprint. The outer key gates the whole select, the column keys refine it.

```sql
/*Wrapping*/?SELECT Id!, Username FROM users
```

With `Wrapping` off the whole select is gone, always-kept columns included.

## Across UNION

Matching column names across `?SELECT`s share one key, keeping the projections in sync.

```sql
?SELECT ArtistId AS Id, Name FROM artists UNION ALL ?SELECT GenreId AS Id, Name FROM genres

-- Name on
SELECT Name FROM artists UNION ALL SELECT Name FROM genres
```

Different names get different keys, so mismatched aliases can produce an invalid side. Align the aliases.

## In a CTE

```sql
WITH a AS (?SELECT AlbumId AS Id, Title, ArtistId FROM albums) SELECT * FROM a

-- Title on
WITH a AS (SELECT Title FROM albums) SELECT * FROM a
```

## Modifiers and `???`

A modifier before the first column is swept into that column's footprint. Isolate it with the [`???` boundary](conditional-markers.md#the--boundary).

```sql
?SELECT DISTINCT Title, Composer FROM tracks

-- only Composer on
SELECT Composer FROM tracks
```

```sql
?SELECT DISTINCT ??? Title, Composer FROM tracks

-- only Composer on
SELECT DISTINCT Composer FROM tracks
```

## Mapping note

A command whose projection changes produces several result schemas. That is expected and supported: the row mapper is chosen per schema. Ask for a type whose members are all optional to fit, or a [DynaObject](../mapping/dynaobject.md) when the shape is open-ended.
