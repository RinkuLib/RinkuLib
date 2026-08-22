# Result shapes

The requested type chooses the result behavior.

```csharp
Album first = GetAlbums.Query<Album>(cnn);
Optional<Album> maybe = GetAlbums.Query<Optional<Album>>(cnn);
Single<Album> exactlyOne = GetAlbums.Query<Single<Album>>(cnn);
List<Album> all = GetAlbums.Query<List<Album>>(cnn);
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn);
```

## First result

```csharp
Album album = GetAlbums.Query<Album>(cnn);
// No result raises RINKU4001.
```

A complete mapped result can consume several database rows when the type contains a grouped nested collection.

## First result or none

```csharp
Optional<Album> result = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });

if (result.HasValue)
{
    Album album = result;
    Console.WriteLine(album.Title);
}
```

A reference type can also be read directly as a nullable value.

```csharp
Album? album = FindAlbum.Query<Optional<Album>>(cnn, new { albumId = 999 });
```

Use `OptionalStruct<T>` for a value type.

```csharp
int? count = FindCount.Query<OptionalStruct<int>>(cnn);
```

## Exactly one result

```csharp
Album album = GetAlbum.Query<Single<Album>>(cnn);
// No result raises RINKU4001.
// A second result raises RINKU4002.
```

## Zero or one result

```csharp
Album? album = FindAlbum.Query<SingleOrDefault<Album>>(cnn, new { albumId = 999 });
// A second result raises RINKU4002.
```

Use `SingleOrDefaultStruct<T>` for value types.

```csharp
int? count = FindCount.Query<SingleOrDefaultStruct<int>>(cnn);
```

## Buffered collections

```csharp
List<Album> list = GetAlbums.Query<List<Album>>(cnn);
Album[] array = GetAlbums.Query<Album[]>(cnn);
```

No rows produces an empty list or array.

## Synchronous stream

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

foreach (Album album in albums)
    Console.WriteLine(album.Title);
```

The command remains active while the sequence is being enumerated. See [streaming](streaming.md).

## Present database NULL

```csharp
string? title = GetNullableTitle.Query<MaybeNull<string>>(cnn);
int? year = GetNullableYear.Query<int?>(cnn);
```

These shapes accept a present database `NULL`. No result still raises `RINKU4001`.

## No result or database NULL

```csharp
OptionalNullable<string> title = FindNullableTitle.Query<OptionalNullable<string>>(cnn);
OptionalNullableStruct<int> year = FindNullableYear.Query<OptionalNullableStruct<int>>(cnn);
```

These shapes accept both no result and a present database `NULL`.

## Exactly one result including database NULL

```csharp
Single<MaybeNull<string>> title = GetOneNullableTitle.Query<Single<MaybeNull<string>>>(cnn);
Single<int?> year = GetOneNullableYear.Query<Single<int?>>(cnn);
```

The outer `Single<T>` still checks the result count.

## At most one result including database NULL

```csharp
SingleOrDefaultNullable<string> title = FindOneNullableTitle.Query<SingleOrDefaultNullable<string>>(cnn);
SingleOrDefaultNullableStruct<int> year = FindOneNullableYear.Query<SingleOrDefaultNullableStruct<int>>(cnn);
```

Receiving a second result raises the `RINKU4002` error.

## Scalars

```csharp
int count = CountAlbums.Query<int>(cnn);
```

The default scalar registration reads the first column.

## Tuples

```csharp
(int id, string title) = GetAlbumSummary.Query<(int, string)>(cnn);
```

See [tuples](../mapping/tuples.md) for sequential reading and repeated mapped types.

## Runtime result type

Use a runtime `Type` when the result shape is selected dynamically.

```csharp
Type resultType = typeof(Album);
object? result = parser.Query(resultType, command);
```

See [fixed result schema](fixed-result-schema.md) for the non generic `CachedTypeParser` and its runtime type overloads.

Use [database NULL](../mapping/nulls.md) for column null behavior. Use [custom result parsers](../customization/result-parsers.md) when the built in result count shapes do not describe the required behavior.
