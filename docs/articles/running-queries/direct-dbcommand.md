# Any DbCommand

The mapping side is not tied to `QueryCommand`. The same `Query`, `Execute`, and `ExecuteScalar` extensions sit directly on `DbCommand` (and `IDbCommand`), so a command you built yourself maps the same way.

```csharp
using var cmd = cnn.CreateCommand();
cmd.CommandText = "SELECT TrackId AS Id, Name FROM tracks WHERE TrackId = @id";
cmd.Parameters.Add(new SqlParameter("@id", 10));

Track track = cmd.Query<Track>();
```

The call runs the command, opens and closes the connection if it was closed, and maps the result. By default it also disposes the command when done (`disposeCommand: true`). A streamed result (`IEnumerable<T>`, `IAsyncEnumerable<T>`) takes ownership of the reader and command and releases them when enumeration finishes.

This plain form derives the row mapper from the result columns on every call. Fine for occasional use. When the same command shape runs hot, hold on to the work:

## Hold a parser

A parser is the compiled row reader for one result schema. Get one once, pass it back in.

```csharp
ITypeParser<Track> parser = reader.GetParser<Track>();   // from any DbDataReader

Track track = cmd.Query<Track>(parser: parser);          // no per-call derivation
```

## Hold a self-filling cache

`SingleParser<T>` pairs one SQL string with the parser it learns on first use. `StoredProcParser<T>` does the same for a stored procedure.

```csharp
static readonly SingleParser<List<Track>> TopTracks =
    new("SELECT TOP 10 TrackId AS Id, Name FROM tracks ORDER BY UnitPrice DESC");

using var cmd = cnn.CreateCommand();
List<Track> tracks = TopTracks.Query(cmd);   // sets CommandText, learns the parser once

static readonly StoredProcParser<List<Invoice>> Archive = new("dbo.GetArchivedInvoices");
```

## A schema known at compile time

When a type's members describe the result columns, the parser can be built without ever looking at a reader. `Query<TSchema, T>` uses `TSchema` as the schema and maps to `T`.

```csharp
// GetTracksByAlbumResult mirrors the query's columns (generated types fit naturally here)
List<GetTracksByAlbumResult> tracks =
    cnn.GetTracksByAlbum(albumId: 1).Query<GetTracksByAlbumResult, List<GetTracksByAlbumResult>>();
```

The schema comes from `TSchema`'s constructor parameters or members. A type can also state it explicitly by implementing `ISchemaProvider`. This is the natural companion to [code generation](../codegen/index.md), whose generated result records are exact column mirrors.

## `IDbCommand` support

The same surface is mirrored for `IDbCommand`. When the instance is really a `DbCommand`, async calls forward to the real async implementations. Otherwise they run synchronously and return completed tasks.
