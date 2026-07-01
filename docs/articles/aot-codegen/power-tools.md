# RinkuPowerTools

*Generate `DbCommand` factories from your database, in Visual Studio.*

RinkuPowerTools is a Visual Studio extension. You describe your queries once, and it connects to the database, infers each query's parameters and result columns, and generates strongly-typed C# you check in next to your code. SQL Server today.

## The workflow

1. **Configure.** From the **Rinku Power Tools** menu, set a connection and a list of queries (the Config and Query managers write a [`rinkupt.json`](configuration.md) next to your project).
2. **Generate.** *Refresh* or *Update* runs each query through SQL Server's metadata, then writes the output. *Refresh all* regenerates everything.
3. **Use.** Call the generated method and map the result.

The menu commands are **Configure** (open the UI), **Refresh** (regenerate the current item), **Update** (refresh the generated file), and **Refresh all** (regenerate every query).

## What it generates

For a query `GetTracksByAlbum` with one parameter, it writes `DbCommands.rinku.cs`.

```csharp
namespace MyApp.Data;

public static class DbCommands {
    /// <Command cref="GetTracksByAlbumResult" />
    public static DbCommand GetTracksByAlbum(this DbConnection connection, int albumId) {
        var command = connection.CreateCommand();
        command.CommandText = @"SELECT TrackId AS Id, Name AS ""Track Name"", UnitPrice FROM tracks WHERE AlbumId >= @albumId";
        command.CommandType = CommandType.Text;
        command.Add("@albumId", DbType.Int32, albumId);
        return command;
    }
}

/// <Schema LastUpdated="2026-06-28T14:05" />
public partial record GetTracksByAlbumResult(int Id, [TrueName("Track Name")] string TrackName, decimal UnitPrice);
```

A few rules the generator follows.

- **Return shape.** No columns becomes a `void`-style command. One simple column becomes that scalar type. Otherwise it becomes a `{MethodName}Result` record (or your configured `ResultSetName`).
- **Stable output.** Regenerating copies through unchanged result records and only rewrites the ones whose columns actually changed.
- **Resilient.** A query that fails to introspect emits a `#error` block with the details, and the rest still generate.
- **Naming.** When a column name isn't a clean C# identifier (the `"Track Name"` alias above), the record gets a `[TrueName("...")]` so mapping still lines up.

## Using the output

The generated method hangs off `DbConnection`. Pair it with the [mapping engine](../mapping/index.md) to get objects.

```csharp
using var cnn = new SqlConnection(connectionString);
List<GetTracksByAlbumResult> tracks =
    cnn.GetTracksByAlbum(1).Query<List<GetTracksByAlbumResult>>();
```

`GetTracksByAlbum(1)` is generated ADO.NET (no Rinku needed to build it). `.Query<List<...>>()` is the engine mapping the rows. The result record carries a `<Schema>` tag, which the [analyzer](analyzer.md) uses to flag a hand-written type that drifts out of sync.
