# Streaming

```csharp
public record Album(int Id, string Title);
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId ORDER BY AlbumId");
```

## Synchronous stream

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 7 });

foreach (Album album in albums)
    Console.WriteLine(album.Title);
```

The reader remains active while the sequence is enumerated.

```csharp
using IEnumerator<Album> e = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 7 }).GetEnumerator();

while (e.MoveNext())
{
    if (e.Current.Id == 12)
        break;
}
// Disposing the enumerator disposes the active reader.
```

A connection opened for the stream remains in use until the reader is disposed. A connection that was already open stays open.

[Connection lifetime](execution-context.md)

## Async stream

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
{
    if (album.Id == 12)
        break;
}
// Breaking the await foreach disposes the async enumerator.
```

## Output parameters

```csharp
QueryCommand findAlbums = QueryCommand.FromProc("FindAlbums", cnn);
IEnumerable<Album> albums = findAlbums.Query<IEnumerable<Album>>(cnn, out DbCommand command, new { artistId = 7 });

using (command)
{
    foreach (Album album in albums)
        Console.WriteLine(album.Title);

    int total = command.GetOutputValue<int>("@total");
    // The stream is finished before the output value is read.
}
```

[Stored procedure output values](stored-procedures.md)
