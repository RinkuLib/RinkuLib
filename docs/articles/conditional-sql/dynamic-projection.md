# Dynamic projection

Prefixing a `SELECT` with `?` turns each projected column into its own condition, keyed by the column name or alias. It affects only that `SELECT`'s column list.

* **Template:** `?SELECT AlbumId AS Id, Title FROM albums`
* **Equivalent to:** `SELECT /*Id*/AlbumId AS Id, /*Title*/Title FROM albums`
* **Result (`Title` used):** `SELECT Title FROM albums`

Activate the keys like any condition key: `builder.Use("Title")`, or a parameter object with `[ForBoolCond]` members.

## Always-kept columns

A `!` right after the column expression keeps it out of the conditional logic. It is always projected.

* **Template:** `?SELECT AlbumId AS Id!, Title, ArtistId FROM albums`
* **Result (nothing used):** `SELECT AlbumId AS Id FROM albums`
* **Result (`Title` used):** `SELECT AlbumId AS Id, Title FROM albums`

`!` is only valid inside a `?SELECT`. Anywhere else the template throws at construction.

## Joining columns

`&,` welds columns under one key, the last column's name.

* **Template:** `?SELECT ArtistId AS Id&, Name FROM artists`
* **Result (`Name` used):** `SELECT ArtistId AS Id, Name FROM artists`

`Id` is not a key of its own here, it rides with `Name`.

## Adding a key to a column

A marker before a column adds its keys on top of the column's own, combined with "and".

* **Template:** `?SELECT Id, Username, /*Admin*/Email FROM users`
* **Result (`Id`, `Username`, `Email` used, `Admin` not):** `SELECT Id, Username FROM users`

`Email` needs both its own key and `Admin`. A marker on an always-kept column replaces "always" with the marker's keys:

* **Template:** `?SELECT /*Manual*/Id!, Username FROM users`
* `Id` is projected when `Manual` is active, instead of always.

## Under an outer condition

A `?SELECT` can itself sit inside a bigger conditional footprint. The outer key gates the whole select, the column keys refine it.

* **Template:** `/*Wrapping*/?SELECT Id!, Username FROM users`
* **Result (`Wrapping` not used):** the select is gone, always-kept columns included.

## Across UNION

Matching column names across `?SELECT`s share one key, keeping the projections in sync.

* **Template:** `?SELECT ArtistId AS Id, Name FROM artists UNION ALL ?SELECT GenreId AS Id, Name FROM genres`
* **Result (`Name` used):** `SELECT Name FROM artists UNION ALL SELECT Name FROM genres`

Different names get different keys, so mismatched aliases can produce an invalid side. Align the aliases.

## In a CTE

* **Template:** `WITH a AS (?SELECT AlbumId AS Id, Title, ArtistId FROM albums) SELECT * FROM a`
* **Result (`Title` used):** `WITH a AS (SELECT Title FROM albums) SELECT * FROM a`

## Modifiers and `???`

A modifier before the first column is swept into that column's footprint. Isolate it with the [`???` boundary](conditional-markers.md#the--boundary).

* **Template:** `?SELECT DISTINCT Title, Composer FROM tracks` with only `Composer` used gives `SELECT Composer FROM tracks`.
* **Template:** `?SELECT DISTINCT ??? Title, Composer FROM tracks` gives `SELECT DISTINCT Composer FROM tracks`.

## Mapping note

A command whose projection changes produces several result schemas. That is expected and supported: the row mapper is chosen per schema. Ask for a type whose members are all optional to fit, or a [DynaObject](../mapping/dynaobject.md) when the shape is open-ended.
