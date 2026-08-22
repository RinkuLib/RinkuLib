# Raw readers

Use `ExecuteReader` when application code needs the provider `DbDataReader` directly.

```csharp
static readonly QueryCommand GetAlbumRows = new("SELECT AlbumId, Title FROM albums WHERE ArtistId = @artistId");

DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

using (command)
using (reader)
{
    while (reader.Read())
    {
        int id = reader.GetInt32(0);
        string title = reader.GetString(1);
        Console.WriteLine($"{id}: {title}");
    }
}
```

The returned reader is the provider reader. The caller disposes both the reader and generated command.

## Connection ownership

Disposing the reader closes a connection that Rinku opened for the operation. It does not dispose the generated command.

An already open connection remains open. See [execution context](execution-context.md) for the same ownership rules used by other query forms.

## Async reader

```csharp
DbDataReader reader = await GetAlbumRows.ExecuteReaderAsync(cnn, out DbCommand command, new { artistId = 7 }, ct: cancellationToken);

await using (command)
await using (reader)
{
    while (await reader.ReadAsync(cancellationToken))
        Console.WriteLine(reader.GetValue(0));
}
```

Use normal [result shapes](result-shapes.md) when Rinku should map the reader instead.
