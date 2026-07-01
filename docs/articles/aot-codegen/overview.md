# Code generation (AOT)

*A design-time way to get the command, ready to run.*

The mapping engine builds and compiles its readers **at runtime**. Code generation is that same work moved earlier. Instead of assembling a `DbCommand` when the program runs, **RinkuPowerTools** generates `DbCommand` factory methods (and matching result records) from your database schema **ahead of time**. Handy for AOT, or just to see the SQL as real, navigable C#.

> **Status.** RinkuPowerTools is early in development. Today it supports **SQL Server** through a **Visual Studio** extension. Treat this section as the stable surface of that workflow. Broader provider and editor support is future work and is not documented here.

## Where it sits

It produces the `DbCommand`. The [mapping engine](../mapping/index.md) turns that command's rows into objects. The two meet at plain ADO.NET, so you can take either alone or both together.

```csharp
using var cnn = new SqlConnection(connectionString);

// GetTracksByAlbum(...) is generated ADO.NET, no Rinku needed to build the command.
// Query<...> is the engine mapping the rows.
List<GetTracksByAlbumResult> tracks =
    cnn.GetTracksByAlbum(albumId: 1).Query<List<GetTracksByAlbumResult>>();
```

- **PowerTools doesn't depend on Rinku.** It emits a method that creates a `DbCommand`, sets its `CommandText` and `CommandType`, and adds correctly-typed parameters. You could read those commands by hand.
- **Pairing is the natural fit.** Generation gives you the command. The engine gives you the objects.

## The analyzers

A few Roslyn analyzers ship inside the `Rinku` package. They're mostly designed around this workflow, keeping a hand-written type in sync with a generated schema, so you may never notice them. Some can help outside it too, like a small "invoke this method" completion. See [the analyzer note](analyzer.md).

## Pages

- [RinkuPowerTools](power-tools.md). The end-to-end Visual Studio workflow.
- [Configuration](configuration.md). The `rinkupt.json` file.
- [Analyzer note](analyzer.md). The invoke completion and the sync diagnostics.
