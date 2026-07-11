# RinkuPowerTools

A Visual Studio extension that generates strongly-typed ADO.NET from your SQL. You list your queries once, it connects to the database, infers each query's parameters and result columns, and generates C# you check in.

SQL Server and Visual Studio only for now.

## The workflow

1. **Configure.** From the **Rinku Power Tools** menu, set a connection and a list of queries. The managers write a `rinkupt.json` next to your project.
2. **Generate.** *Refresh* regenerates the current item, *Refresh all* regenerates everything, *Update* refreshes the generated file.
3. **Use.** Call the generated method, on its own or mapped through the [Rinku](https://www.nuget.org/packages/Rinku) package.

For a query named `GetTracksByAlbum` with one parameter, the generated method builds the `DbCommand` with no dependency on Rinku:

```csharp
public static class DbCommands {
    /// <Command cref="GetTracksByAlbumResult" />
    public static DbCommand GetTracksByAlbum(this DbConnection connection, int albumId) {
        var command = connection.CreateCommand();
        command.CommandText = @"SELECT TrackId AS Id, Name AS [Track Name], UnitPrice FROM tracks WHERE AlbumId >= @albumId";
        command.CommandType = CommandType.Text;
        command.Add("@albumId", DbType.Int32, albumId);
        return command;
    }
}

/// <Schema LastUpdated="2026-06-28T14:05" />
public partial record GetTracksByAlbumResult(int Id, [TrueName("Track Name")] string TrackName, decimal UnitPrice);
```

Documentation: [rinkulib.github.io/RinkuLib](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html)
