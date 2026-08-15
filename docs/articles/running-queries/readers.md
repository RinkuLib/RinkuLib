# Raw readers

Use `ExecuteReader` when application code needs the `DbDataReader` directly.

```csharp
static readonly QueryCommand GetAlbumRows = new("SELECT AlbumId, Title FROM albums WHERE ArtistId = @artistId");

DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

using (command) {
    using (reader) {
        while (reader.Read()) {
            int id = reader.GetInt32(0);
            string title = reader.GetString(1);
            Show(id, title);
        }
    }
}
```

The generated-command form returns the command through `out DbCommand`. The caller disposes both objects.

The returned object is the provider's `DbDataReader`, not a Rinku wrapper.

```csharp
DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

try {
    while (reader.Read())
        Show(reader.GetInt32(0), reader.GetString(1));
}
finally {
    reader.Dispose();
    command.Dispose();
}
```

Disposing the reader closes a connection opened by Rinku, but does not dispose the command.

```csharp
using DbConnection cnn = GetConnection(); // closed

DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command);
reader.Dispose();

// cnn is closed again.
// command still belongs to the caller.
command.Dispose();
```

An initially open connection remains open.

```csharp
using DbConnection cnn = GetConnection();
cnn.Open();

DbDataReader reader = GetAlbumRows.ExecuteReader(cnn, out DbCommand command);
using (command) {
    using (reader) {
        while (reader.Read())
            Show(reader);
    }
}

// cnn remains open.
```

The asynchronous form has the same ownership.

```csharp
DbDataReader reader = await GetAlbumRows.ExecuteReaderAsync(cnn, out DbCommand command, new { artistId = 7 }, ct: cancellationToken);

await using (command) {
    await using (reader) {
        while (await reader.ReadAsync(cancellationToken))
            Show(reader);
    }
}
```
