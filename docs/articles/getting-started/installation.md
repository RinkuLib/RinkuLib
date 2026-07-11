# Installation

RinkuLib ships on NuGet as **`Rinku`**.

```bash
dotnet add package Rinku
```

```powershell
Install-Package Rinku
```

It targets **.NET 8** and **.NET 10**. That one package is the whole core, the mapping engine and the SQL templating. A set of Roslyn analyzers ships inside the package too, no separate install (see [analyzers](../codegen/analyzers.md)).

**RinkuPowerTools** is a separate, optional Visual Studio extension that generates `DbCommand` factory methods from your database schema. SQL Server and Visual Studio only for now. See [code generation](../codegen/index.md).

## Verifying the install

```csharp
using RinkuLib.Queries;
using RinkuLib.Commands;

var cmd = new QueryCommand("SELECT 1");
using DbConnection cnn = GetConnection();
int one = cmd.Query<int>(cnn);   // 1
```

If that compiles and runs, you are ready. Continue to the [quick start](quick-start.md).
