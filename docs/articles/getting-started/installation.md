# Installation

## The core package

RinkuLib ships on NuGet as **`Rinku`**.

```bash
dotnet add package Rinku
```

```powershell
Install-Package Rinku
```

It targets **.NET 8** and **.NET 10**. That one package is the whole core, the mapping engine and the SQL templating. There is nothing else to install to get the [quick start](quick-start.md) working.

## Code generation (a second workflow)

The core engine builds and maps commands **at runtime**. There is also an opt-in, design-time option for teams that prefer generated, ahead-of-time code.

- **RinkuPowerTools** is a separate Visual Studio extension that generates ready-to-run `DbCommand` factory methods (and matching result records) from your database schema. It is early in development (SQL Server and Visual Studio today). See [code generation](../aot-codegen/overview.md).
- A set of **Roslyn analyzers** ships *inside* the `Rinku` package. They're mostly designed around the code-generation workflow, keeping generated types in sync, and some can earn their keep outside it too, like an "invoke this method" completion. See [the analyzer note](../aot-codegen/analyzer.md).

## Verifying the install

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

var cmd = new QueryCommand("SELECT 1");
using DbConnection cnn = GetConnection();
int one = cmd.Query<int>(cnn);   // 1
```

If that compiles and runs, you are ready. Continue to the [quick start](quick-start.md).
