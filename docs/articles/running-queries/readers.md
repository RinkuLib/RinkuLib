# Raw readers

```csharp
static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");
```

## DbDataReader

```csharp
DbDataReader reader = GetAlbums.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

using (command)
using (reader)
{
    while (reader.Read())
    {
        int id = reader.GetInt32(0);
        string title = reader.GetString(1);
        Console.WriteLine($"{id} {title}");
    }
}
```

The caller owns the returned reader and command. A connection opened for this reader is closed when the reader is disposed.

## Async reader

```csharp
DbDataReader reader = await GetAlbums.ExecuteReaderAsync(cnn, out DbCommand command, new { artistId = 7 }, ct: cancellationToken);

await using (command)
await using (reader)
{
    while (await reader.ReadAsync(cancellationToken))
        Console.WriteLine(reader.GetString(1));
}
```

## Existing command parser

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";

Album album = AlbumParser.Query(command);
```

[Existing DbCommand](dbcommand.md)
