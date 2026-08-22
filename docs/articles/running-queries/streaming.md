# Streaming

Choose a buffered or streamed result shape based on how the rows will be consumed.

```csharp
List<Album> list = GetAlbums.Query<List<Album>>(cnn);
Album[] array = GetAlbums.Query<Album[]>(cnn);
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn);
IAsyncEnumerable<Album> asyncStream = GetAlbums.StreamQueryAsync<Album>(cnn);
```

`List<T>` and arrays finish reading before `Query` returns.

## Synchronous streaming

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

foreach (Album album in albums)
    Console.WriteLine(album.Title);
```

The reader stays active during enumeration.

If Rinku opened the connection, the connection stays in use until enumeration finishes or the enumerator is disposed. A connection that was already open stays open.

## Stop early

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

using (IEnumerator<Album> iterator = albums.GetEnumerator())
{
    if (iterator.MoveNext())
        Console.WriteLine(iterator.Current.Title);
}
```

Disposing the enumerator disposes the active reader.

## Async streaming

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

Breaking the loop disposes the async enumerator.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken))
{
    Console.WriteLine(album.Title);
    break;
}
```

## Output parameters and active commands

A streamed result can keep output values unavailable until reader work has finished. Dispose or finish the stream before reading provider output parameters.

See [stored procedures](stored-procedures.md) for output values and [execution context](execution-context.md) for connection lifetime.
