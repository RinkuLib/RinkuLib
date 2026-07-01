# Dynamic projection (`?SELECT`)

*Turn projected columns into independent conditional segments.*

Prefixing a `SELECT` with `?` pulls each projected column into its own conditional segment, keyed by the column name (or alias). It affects only the column list of that `SELECT`.

* **Template:** `?SELECT AlbumId AS Id, Title FROM albums`
* **Equivalent to:** `SELECT /*Id*/AlbumId AS Id, /*Title*/Title FROM albums`
* **Result (`Title` provided):** `SELECT Title FROM albums`

It works at lower levels too, such as inside a CTE.

* **Template:** `WITH A AS (?SELECT AlbumId AS Id, Title, ArtistId FROM albums) SELECT * FROM A`
* **Result (`Title` provided):** `WITH A AS (SELECT Title FROM albums) SELECT * FROM A`

## Sharing across UNION

When column names match across `SELECT`s, their conditions are **shared**, which keeps the projections in sync.

* **Template:** `?SELECT ArtistId AS Id, Name FROM artists UNION ALL ?SELECT GenreId AS Id, Name FROM genres`
* **Result (`Name` provided):** `SELECT Name FROM artists UNION ALL SELECT Name FROM genres`

If names differ, conditions differ too, so watch for invalid SQL.

* **Template:** `?SELECT ArtistId AS Id, Name FROM artists UNION ALL ?SELECT GenreId AS Ref, Name FROM genres`
* **Result (`Id` provided):** `SELECT ArtistId AS Id FROM artists UNION ALL SELECT FROM genres`

## Modifiers and `???`

A modifier before the first column can be swept into that column's condition. Use `???` to isolate it.

* **Template:** `?SELECT DISTINCT Title, Composer FROM tracks` -> **Result:** `SELECT Composer FROM tracks`
* **Template:** `?SELECT DISTINCT ??? Title, Composer FROM tracks` -> **Result:** `SELECT DISTINCT Composer FROM tracks`

## Joined columns

Joining columns with `&` shares their footprint, and the **last** column name becomes the key.

* **Template:** `?SELECT ArtistId AS Id&, Name FROM artists`
* **Result (`Name` used):** `SELECT ArtistId AS Id, Name FROM artists`

`Id` is not a separate condition because it's joined with `Name`.

## Always-used columns (`!`)

Prefix a column with `!` to keep it **out** of the conditional logic. It's always projected, never pruned, handy for a key column you always need while the rest of the projection stays optional.

* **Template:** `?SELECT !AlbumId AS Id, Title, ArtistId FROM albums`
* **Result (nothing provided):** `SELECT AlbumId AS Id FROM albums`
* **Result (`Title` provided):** `SELECT AlbumId AS Id, Title FROM albums`

The `!` prefix is only valid inside a `?SELECT`. Using it elsewhere throws at compile time.
