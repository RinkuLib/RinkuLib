# Add queries

Open the configuration and add a query. Each entry produces one generated command method.

The available command sources and parameter type suggestions follow the configuration database. SQL Server and PostgreSQL offer stored procedures. SQLite offers SQL queries and SQL files.


## SQL text

Choose `SQL Query` when the SQL belongs in the configuration.

```text
Method Name    GetAlbumsByArtist
Command Type   SQL Query
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

The configuration stores the same query as `SQLQuery`.

```json
{
  "MethodName": "GetAlbumsByArtist",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
}
```

CodeGen inspects the command against the configured database and discovers the input parameters and returned columns.

## Stored procedure

Choose `Stored procedure` and select or enter the procedure name.

```text
Method Name             ArchiveInvoices
Command Type            Stored procedure
Stored Procedure Name   dbo.ArchiveInvoices
```

```json
{
  "MethodName": "ArchiveInvoices",
  "StoredProcName": "dbo.ArchiveInvoices"
}
```

Stored procedure suggestions come from the configured database connection.

## SQL file

Choose `SQL File` when SQL should stay in its own file.

```text
Method Name    GetOpenInvoices
Command Type   SQL File
SQL File       Sql/GetOpenInvoices.sql
```

```json
{
  "MethodName": "GetOpenInvoices",
  "SQLFile": "Sql/GetOpenInvoices.sql"
}
```

The file picker stores the path automatically. A file inside the project is stored as a project relative path. A file outside the project is stored as an absolute path.

```text
C:\Projects\MyApp\Sql\GetOpenInvoices.sql
    -> Sql/GetOpenInvoices.sql

D:\SharedSql\GetOpenInvoices.sql
    -> D:\SharedSql\GetOpenInvoices.sql
```

When `rinkupt.json` is edited directly, the stored value is authoritative. Relative means project owned and absolute means external.

A relative file stays as the generated runtime key. The file is copied to build and publish output so the same relative path can be read from the application directory when the cache does not already contain a value.

```text
Sql/GetOpenInvoices.sql
```

An absolute file keeps its absolute path and is not copied into the application output.

```text
D:\SharedSql\GetOpenInvoices.sql
```

CodeGen reads the file during generation so it can inspect parameters and result columns. The generated command keeps the configured file reference instead of copying the SQL text into generated C#.

At runtime the shared `RinkuPowerTools.SqlFiles` dictionary can replace or remove the current SQL for that key. See [Generated commands](generated-code.md) for the generated call and dictionary behavior.

## Name a generated result

Several returned columns normally generate a record named from the method.

```csharp
public partial record GetAlbumsByArtistResult(int Id, string Title);
```

Set `Result Set Name` when another name is clearer.

```text
Method Name       GetAlbumsByArtist
Result Set Name   AlbumRow
```

```csharp
public partial record AlbumRow(int Id, string Title);
```

See [Generated commands](generated-code.md) for result naming and scalar results.

## Correct parameter metadata

Most parameters are discovered from the database. Add a parameter correction when the discovered type or null behavior needs to be changed.

```text
Name          @cutoff
Type          datetime2
Is Nullable   false
```

```json
{
  "MethodName": "ArchiveInvoices",
  "StoredProcName": "dbo.ArchiveInvoices",
  "Parameters": [
    {
      "Name": "@cutoff",
      "Type": "datetime2",
      "IsNullable": false
    }
  ]
}
```

A correction applies to the named discovered parameter. It does not replace the command definition.

## Provider parameter behavior

SQL Server and PostgreSQL discover parameter metadata from the database when possible. PostgreSQL also preserves its native type name when common `DbType` metadata is not enough.

PostgreSQL positional SQL is supported. `$1`, `$2`, and later parameters generate valid C# names such as `p1` and are added to the Npgsql parameter collection in positional order. Do not mix PostgreSQL positional parameters with named `@name` or `:name` parameters in the same query.

SQLite query parameters start untyped because SQLite does not declare parameter types. Add a correction when a generated argument should be strongly typed.

```json
{
  "Name": "$id",
  "Type": "integer",
  "IsNullable": false
}
```

Without the correction, PowerTools keeps the parameter as `object?` and does not emit `DbType.Object`.

## Remove a query

Delete the query from the query manager and refresh the configuration.

```text
Delete query
Refresh configuration
Generated method is removed
```

See [Refresh generated code](refresh.md) for regeneration behavior.
