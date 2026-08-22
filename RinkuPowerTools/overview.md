# Rinku Power Tools

Generate strongly typed ADO.NET code directly from SQL in Visual Studio.

Rinku Power Tools lets you define SQL queries, stored procedures, and SQL files using a Visual Studio interface. It generates C# methods that create fully configured `DbCommand` instances with parameters already defined and bound.

This removes repetitive ADO.NET boilerplate while keeping your SQL explicit and under your control.

## How it works

1. Configure a database connection for your project.
2. Create a query, stored procedure, or SQL file from Visual Studio.
3. Rinku Power Tools inspects the query and its result shape.
4. Generated C# code provides typed command methods and result records.
5. Refresh the generated code whenever the SQL changes.

For example, a query named `GetUsers` can generate a method like:

```csharp
DbCommand GetUsers(
    this DbConnection connection,
    int? ID,
    string? Username,
    bool Valid);
```

The method creates the command, sets the SQL text, and adds the required parameters automatically.

## Generated result types

When a query returns multiple columns, a strongly typed result record is generated for the first result set:

```csharp
public record GetUsersResult(
    int ID,
    string Username,
    string Email,
    bool Valid);
```

You can use the generated record directly, or generate a reusable C# class from the result shape when you need your own model type.

## Query sources

Rinku Power Tools supports:

- SQL statements
- Stored procedures
- External `.sql` files
- Multiple result sets
- Parameterized queries
- Query configuration stored in `rinkupt.json`

## Automatic parameters

Parameters are inferred from the SQL whenever possible. Optional overrides let you refine the generated signature when database metadata is ambiguous or does not match your intended API.

For example, you can:

- Change `VARCHAR(MAX)` to `VARCHAR(150)`
- Map `TINYINT` to `int`
- Make a nullable `BIT` parameter non-nullable
- Adjust parameter names, types, nullability, or size

## Connection resolution

The extension can resolve database connections from common .NET project sources, including:

- `appsettings.json`
- User Secrets
- Raw connection strings
- `.csproj` properties
- `launchSettings.json`

## Works with or without Rinku

Rinku Power Tools is designed to work with the [Rinku NuGet package](https://www.nuget.org/packages/Rinku/), which provides mapping, materialization, and query execution features.

The generated code also works with plain ADO.NET and any `DbConnection`. Rinku is optional.

## Learn more

- [Install from the Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=Rinku.RinkuPowerTools)
- [Rinku documentation](https://rinkulib.github.io/RinkuLib/articles/index.html)
- [Code generation guide](https://rinkulib.github.io/RinkuLib/articles/codegen/index.html)
- [Configuring code generation](https://rinkulib.github.io/RinkuLib/articles/codegen/configure.html)
- [Generated code](https://rinkulib.github.io/RinkuLib/articles/codegen/generated-code.html)
- [Refreshing generated code](https://rinkulib.github.io/RinkuLib/articles/codegen/refresh.html)
- [RinkuLib on GitHub](https://github.com/RinkuLib/RinkuLib)
