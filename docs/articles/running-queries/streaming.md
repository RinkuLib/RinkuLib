# Streaming

Rinku includes buffered and streamed results for common collection use. Choose based on how the results will be read. A custom parser can use another approach.

```csharp
List<Album> list = GetAlbums.Query<List<Album>>(cnn);
Album[] array = GetAlbums.Query<Album[]>(cnn);
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn);
IAsyncEnumerable<Album> asyncStream = GetAlbums.StreamQueryAsync<Album>(cnn);
```

`List<T>` and arrays finish reading before `Query` returns. Their connection and command work is complete.

```csharp
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);

foreach (Album album in albums)
    Show(album);
```

`IEnumerable<T>` keeps the reader active while the sequence is enumerated.

```csharp
IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

foreach (Album album in albums)
    Show(album);
```

If the connection was closed, it remains in use until enumeration finishes or the enumerator is disposed. An initially open connection remains open.

## Stop a synchronous stream early

Disposing the enumerator immediately disposes the reader. Remaining rows do not need to be read by the application.

```csharp
using DbConnection cnn = GetConnection(); // closed

IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

using (IEnumerator<Album> iterator = albums.GetEnumerator()) {
    if (iterator.MoveNext())
        Show(iterator.Current);
}

// The reader is disposed and the connection is closed again.
```

With an initially open connection, only the reader is closed.

```csharp
using DbConnection cnn = GetConnection();
cnn.Open();

IEnumerable<Album> albums = GetAlbums.Query<IEnumerable<Album>>(cnn);

using (IEnumerator<Album> iterator = albums.GetEnumerator()) {
    if (iterator.MoveNext())
        Show(iterator.Current);
}

// cnn remains open.
```

## Read rows asynchronously

Use `StreamQueryAsync<T>` when row consumption should be asynchronous.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken)) {
    Show(album);
}
```

Breaking an `await foreach` disposes its async enumerator.

```csharp
await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, ct: cancellationToken)) {
    Show(album);
    break;
}
// The reader is disposed here.
```

`QueryAsync<IEnumerable<T>>` only starts the command asynchronously. The returned sequence is still consumed synchronously. Prefer `StreamQueryAsync<T>` when asynchronous enumeration is required.

## Output parameters

Use an overload that returns the `DbCommand` when output values are needed.

```csharp
static readonly QueryCommand ReadAndCountAlbums = QueryCommand.FromProc("ReadAndCountAlbums", setupConnection);

IEnumerable<Album> albums = ReadAndCountAlbums.Query<IEnumerable<Album>>(cnn, out DbCommand command);

using (command) {
    using (IEnumerator<Album> iterator = albums.GetEnumerator()) {
        if (iterator.MoveNext())
            Show(iterator.Current);
    }

    int moved = command.GetOutputValue<int>("@moved");
}
```

Disposing the enumerator closes the reader, so output and return values are available even when application enumeration stops early. The provider may consume pending results while closing the reader.

## Command ownership

A normal `QueryCommand` stream owns its generated command and disposes it with the iterator. An overload that returns the command leaves disposal to the caller.

An existing caller-owned `DbCommand` follows its `disposeCommand` argument.

```csharp
IEnumerable<Album> albums = parser.Query(command, disposeCommand: false);
// Disposing the iterator closes the reader but leaves command alive.
```

[Add transactions, timeouts, and cancellation](execution-context.md).
