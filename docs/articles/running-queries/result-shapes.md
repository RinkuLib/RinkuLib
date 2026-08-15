# Result shapes

`Query<T>` uses `T` to choose an `ITypeParser<T>`. The parsers included with Rinku decide how many rows to read, whether to buffer them, and what to do when no result exists. You can add other parsers. This page covers the included parsers.

## First result

```csharp
Album album = GetAlbums.Query<Album>(cnn);
// Returns the first complete Album. No result raises RINKU4001.
```

One result may consume several rows when it contains a nested collection.

```csharp
Artist artist = GetArtistsWithAlbums.Query<Artist>(cnn);
// Several joined rows can build the first Artist and its Albums.
```

## First result or none

```csharp
Optional<Album> result = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });

if (result.HasValue) {
    Album album = result;
    Show(album);
}
```

`Optional<T>` takes the first result without checking for another one. Use `OptionalStruct<T>` when `T` is a value type.

## Exactly one result

```csharp
Album album = GetAlbum.Query<Single<Album>>(cnn);
// No result raises RINKU4001. A second result raises RINKU4002.
```

## Zero or one result

```csharp
Album? album = FindAlbum.Query<SingleOrDefault<Album>>(cnn, new { albumId = 999 });
// No result becomes null. A second result raises RINKU4002.
```

Use `SingleOrDefaultStruct<T>` for a value type.

```csharp
SingleOrDefaultStruct<int> count = FindCount.Query<SingleOrDefaultStruct<int>>(cnn);
```

## Buffered results

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
// No results returns an empty list.
```

Arrays use the same buffered behavior as lists.

```csharp
Album[] albums = GetAlbums.Query<Album[]>(cnn);
// No results returns an empty array.
```

## Synchronous stream

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

foreach (Album album in albums)
    Show(album);
```

The command remains active while the sequence is enumerated. Errors and output parameters can therefore arrive during or after enumeration.

See [streaming](streaming.md) for disposal, output parameters, and asynchronous row consumption.

## Database NULL

Row absence and database `NULL` are separate choices.

### A present result may be NULL

Use `MaybeNull<T>` for a reference type and `T?` for a value type.

```csharp
string? title = GetNullableTitle.Query<MaybeNull<string>>(cnn);
int? year = GetNullableYear.Query<int?>(cnn);
// No result still raises RINKU4001.
```

### No result or NULL

Use `OptionalNullable<T>` for a reference type and `OptionalNullableStruct<T>` for a value type.

```csharp
OptionalNullable<string> title = FindNullableTitle.Query<OptionalNullable<string>>(cnn);
OptionalNullableStruct<int> year = FindNullableYear.Query<OptionalNullableStruct<int>>(cnn);
// Accepts no result or a present database NULL.
```

### Exactly one result, including NULL

Put the null-accepting shape inside `Single<T>`.

```csharp
Single<MaybeNull<string>> title = GetOneNullableTitle.Query<Single<MaybeNull<string>>>(cnn);
Single<int?> year = GetOneNullableYear.Query<Single<int?>>(cnn);
// No result or a second result still raises an error.
```

### At most one result, including NULL

Use `SingleOrDefaultNullable<T>` for a reference type and `SingleOrDefaultNullableStruct<T>` for a value type.

```csharp
SingleOrDefaultNullable<string> title = FindOneNullableTitle.Query<SingleOrDefaultNullable<string>>(cnn);
SingleOrDefaultNullableStruct<int> year = FindOneNullableYear.Query<SingleOrDefaultNullableStruct<int>>(cnn);
// Accepts no result or database NULL. A second result raises RINKU4002.
```

Rinku includes these wrappers for result counts and database `NULL` values.

| Required behavior | Reference-type shape | Value-type shape |
| --- | --- | --- |
| Present value, including `NULL` | `MaybeNull<T>` | `T?` |
| No result or `NULL` | `OptionalNullable<T>` | `OptionalNullableStruct<T>` |
| Exactly one result | `Single<T>` | `Single<T>` |
| At most one result | `SingleOrDefault<T>` | `SingleOrDefaultStruct<T>` |
| At most one result, including `NULL` | `SingleOrDefaultNullable<T>` | `SingleOrDefaultNullableStruct<T>` |

An object that collapses through `[AbortOnNull]` reaches its containing result slot in the same way.

```csharp
OptionalNullable<Album> album = FindOuterJoinedAlbum.Query<OptionalNullable<Album>>(cnn);
// Accepts no result or a collapsed Album.
```

The [database NULL guide](../mapping/nulls.md) covers column rules and collapsed nested objects.

## Scalars

With the default mapping registration, a scalar result reads the first column.

```csharp
int count = CountAlbums.Query<int>(cnn);
```

```sql
SELECT COUNT(*) FROM albums
```

## Tuples

The built-in tuple registration combines several mapped values into one result.

```csharp
(int id, string title) = GetAlbumSummary.Query<(int, string)>(cnn);
```

The [tuple guide](../mapping/tuples.md) covers sequential reading and repeated object types.

## Custom result shapes

Use a [complete-result parser](../customization/result-parsers.md) when the included shapes do not describe how many results to read or what to return when none exist. Use a [type registration](../customization/type-registration.md) when returned columns need another mapping rule.
