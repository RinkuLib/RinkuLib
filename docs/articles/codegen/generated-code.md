# Generated commands

Each configured query generates a `DbCommand` extension method.

```json
{
  "MethodName": "GetAlbumsByArtist",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
}
```

The generated method has this shape.

```csharp
public static partial class DbCommands
{
    public static DbCommand GetAlbumsByArtist(this DbConnection connection, int artistId)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";
        command.CommandType = CommandType.Text;

        command.Add("@artistId", DbType.Int32, artistId);
        return command;
    }
}
```

The exact parameter type, size, precision, scale, direction, and null handling come from discovered database metadata when the provider exposes it. SQLite parameters remain value inferred unless a configuration correction supplies a type.

## SQL files stay referenced

An `SQLFile` is inspected during generation, but its SQL is not copied into the generated command method.

```json
{
  "MethodName": "GetAlbums",
  "SQLFile": "Sql/GetAlbums.sql"
}
```

The generated method keeps the configured path and gets the current SQL through `RinkuPowerTools.GetSqlFile`.

```csharp
public static partial class DbCommands
{
    public static DbCommand GetAlbums(this DbConnection connection)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = RinkuPowerTools.GetSqlFile("Sql/GetAlbums.sql");
        command.CommandType = CommandType.Text;
        return command;
    }
}
```

The shared generated support file contains the existing command extensions and the runtime SQL state.

```csharp
public static class RinkuPowerTools
{
    public static readonly ConcurrentDictionary<string, string> SqlFiles = new(StringComparer.OrdinalIgnoreCase);

    public static string GetSqlFile(string path) => SqlFiles.GetOrAdd(path, static path => File.ReadAllText(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path)));

    // Generated DbCommand extension methods are also in this class.
}
```

Application code can access the same dictionary directly.

```csharp
RinkuPowerTools.SqlFiles["Sql/GetAlbums.sql"] = replacementSql;
```

The next generated command uses the replacement value without reading the file.

Remove an entry when the next command should reload the file.

```csharp
RinkuPowerTools.SqlFiles.TryRemove("Sql/GetAlbums.sql", out _);

using DbCommand command = cnn.GetAlbums();
```

`SqlFiles` uses case insensitive keys. The configured path itself remains the key and PowerTools does not normalize it at runtime.

Relative paths are read from `AppContext.BaseDirectory` only when the dictionary does not already contain the key. CodeGen copies relative SQL files to build and publish output with the same relative path.

Absolute paths remain absolute and are not copied.

```csharp
RinkuPowerTools.SqlFiles[@"D:\SharedSql\GetAlbums.sql"] = replacementSql;
```

A dictionary change affects commands created afterward. A `DbCommand` that was already created keeps its current `CommandText`.

Changing SQL at runtime does not regenerate parameters or result records. The new SQL is expected to remain compatible with the generated command contract.
## Use the command with Rinku

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The generated command is a normal `DbCommand`, so it is not tied to the Rinku result parser.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

See [existing DbCommand](../running-queries/dbcommand.md) for the Rinku parser forms that accept a command directly.

## Result records

A query with several returned columns generates a partial record.

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumsByArtistResult(int Id, string Title);
```

The timestamp changes when the inspected result shape changes. CodeGen preserves an unchanged generated record, including its existing timestamp.

The `Rinku` package analyzers can use that timestamp from application code.

```csharp
/// <BasedOn cref="GetAlbumsByArtistResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

See [Analyzers and code fixes](analyzers.md) for `BasedOn`, `MatchConstructor`, constructor generation, and method invocation generation.

Add application behavior in another partial declaration instead of editing the generated file.

```csharp
public partial record GetAlbumsByArtistResult
{
    public string Display => $"{Id} {Title}";
}
```

## Scalar results

A query that returns one simple column does not need a result record.

`int`, `long`, `short`, `byte`, `string`, `Guid`, `bool`, `decimal`, `double`, `DateTime`, and `float` are supported scalar result types, including nullable forms.

Nullable forms of these types use the same scalar behavior.

```sql
SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId
```

```csharp
static readonly CachedTypeParser<int> CountParser = new();

int count = CountParser.Query(cnn.CountAlbumsByArtist(artistId: 7));
```

The generated method still returns a `DbCommand`. The generated command metadata records that the result shape is `int`.

## Commands without a result

A command with no result columns generates only the command method.

```sql
DELETE FROM albums WHERE AlbumId = @albumId
```

```csharp
using DbCommand command = cnn.DeleteAlbum(albumId: 12);
int affected = command.ExecuteNonQuery();
```

## Several result sets

The generated command can contain SQL that returns several result sets. CodeGen generates result information for the first result set.

```sql
SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId;
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId;
```

The generated method still returns the complete `DbCommand`.

```csharp
using DbCommand command = cnn.GetArtistAndAlbums(artistId: 7);
using DbDataReader reader = command.ExecuteReader();

reader.NextResult();
```

See [multiple result sets](../running-queries/multiple-results.md) for the Rinku result reader when the command is represented by a `QueryCommand`.

## Database names that are not C# names

CodeGen cleans names that cannot be used directly in C# and keeps the database name with `TrueName`.

```sql
SELECT AlbumId, Title AS [Album Title] FROM albums
```

```csharp
public partial record AlbumRow(int AlbumId, [TrueName("Album Title")] string Album_Title);
```

The generated support file defines `TrueNameAttribute` for the project.

## Nullable parameters

Nullable inputs send `DBNull.Value` when the C# value is null.

```csharp
public static partial class DbCommands
{
    public static DbCommand FindAlbums(this DbConnection connection, string? title)
    {
        DbCommand command = connection.CreateCommand();
        command.Add("@title", DbType.String, (object?)title ?? DBNull.Value);
        return command;
    }
}
```

```csharp
DbCommand command = cnn.FindAlbums(title: null);
```

## PostgreSQL native parameter types

PostgreSQL metadata can carry a native database type name in addition to `DbType`. The generated method keeps the shared `DbParameter` creation path and then applies the exact type to the Npgsql parameter.

```csharp
object payload = "{}";
var p_payload = command.Add("@payload", DbType.String, payload);
if (p_payload is not Npgsql.NpgsqlParameter npgsql_p_payload)
    throw new InvalidOperationException("PostgreSQL generated commands require Npgsql parameters.");
npgsql_p_payload.DataTypeName = "jsonb";
```

The application already needs Npgsql to create the PostgreSQL connection. Native typing is emitted only for PostgreSQL parameters whose metadata contains a PostgreSQL type name.

For positional PostgreSQL SQL, the SQL keeps `$1`, `$2`, and later placeholders while the generated parameters are unnamed and added in position order.

```sql
SELECT title FROM album WHERE artist_id = $1
```

```csharp
public static partial class DbCommands
{
    public static DbCommand GetAlbums(this DbConnection connection, int p1)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT title FROM album WHERE artist_id = $1";
        command.CommandType = CommandType.Text;

        command.Add("", DbType.Int32, p1);
        return command;
    }
}
```

## SQLite parameters

An unresolved SQLite parameter does not force `DbType.Object`.

```csharp
public static partial class DbCommands
{
    public static DbCommand GetAlbum(this DbConnection connection, object? id)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, Title FROM Album WHERE Id = $id";
        command.CommandType = CommandType.Text;

        command.Add("$id", (object?)id ?? DBNull.Value);
        return command;
    }
}
```

A parameter correction can make the generated argument and `DbType` explicit.

## Output parameters

Parameters that return a value expose their generated `DbParameter` through an `out` argument.

An input and output parameter keeps its input value and also returns the parameter instance. SQL Server and PostgreSQL stored procedure output parameters use this shape when the provider reports that direction.

```csharp
int counter = 0;
using DbCommand command = cnn.UpdateCounter(counter, out DbParameter out_counter);
command.ExecuteNonQuery();

object updatedCounter = out_counter.Value;
```

A pure output parameter only needs the `out DbParameter` argument.

## Output files

The default configuration generates `DbCommands.rinku.cs` when no command class name is supplied.

```text
.PowerTools.rinku.cs
Data/Generated/DbCommands.rinku.cs
```

The support file is created at the project root. It contains `TrueNameAttribute` and the public `RinkuPowerTools` class with the shared command extensions and SQL file dictionary.

See [Configure CodeGen](configure.md) for output path and namespace settings.
