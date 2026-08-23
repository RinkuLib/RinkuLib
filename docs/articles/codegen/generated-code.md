# Generated commands

## DbCommand method

```json
{
  "MethodName": "GetAlbumsByArtist",
  "SQLQuery": "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId"
}
```

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

Discovered metadata controls generated parameter type, size, precision, scale, direction, and null handling when the provider exposes those values.

## SQL files

```json
{
  "MethodName": "GetAlbums",
  "SQLFile": "Sql/GetAlbums.sql"
}
```

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

The generated support type keeps the runtime SQL cache.

The configured `SQLFile` path stays as the dictionary key. Keys are case insensitive.

```csharp
public static class RinkuPowerTools
{
    public static readonly ConcurrentDictionary<string, string> SqlFiles = new(StringComparer.OrdinalIgnoreCase);

    public static string GetSqlFile(string path)
        => SqlFiles.GetOrAdd(path, static path => File.ReadAllText(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path)));
}
```

Application code can replace the current SQL for a key.

```csharp
RinkuPowerTools.SqlFiles["Sql/GetAlbums.sql"] = replacementSql;
```

Application code can remove the cached value so the next command reads the file again.

```csharp
RinkuPowerTools.SqlFiles.TryRemove("Sql/GetAlbums.sql", out _);
using DbCommand command = cnn.GetAlbums();
```

Relative paths are read from `AppContext.BaseDirectory` when no cached value exists. Relative project files are copied to build and publish output at the same relative path. Absolute paths remain absolute and are not copied.

```csharp
RinkuPowerTools.SqlFiles[@"D:\SharedSql\GetAlbums.sql"] = replacementSql;
```

A command already created keeps the `CommandText` it received. Changing the dictionary affects commands created afterward.

Runtime SQL replacement does not regenerate parameters or result records.

## Parse a generated command

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> Parser = new();

List<GetAlbumsByArtistResult> albums = Parser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

The same generated command can be executed directly.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

[Existing DbCommand](../running-queries/dbcommand.md)

## Result record

```csharp
/// <Schema LastUpdated="2026-08-21T14:00Z" />
public partial record GetAlbumsByArtistResult(int Id, string Title);
```

The schema timestamp changes when the inspected result shape changes. An unchanged record keeps its existing timestamp.

```csharp
/// <BasedOn cref="GetAlbumsByArtistResult" LastUpdated="2026-08-21T14:00Z" />
public record AlbumDto(int Id, string Title);
```

Application members can live in another partial declaration.

```csharp
public partial record GetAlbumsByArtistResult
{
    public string Display => $"{Id} {Title}";
}
```

[Schema analyzers](analyzers.md)

## Scalar result

```sql
SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId
```

```csharp
static readonly CachedTypeParser<int> CountParser = new();
int count = CountParser.Query(cnn.CountAlbumsByArtist(artistId: 7));
```

Scalar result metadata is generated without a result record. Nullable forms use the same scalar path.

## Command without returned columns

```sql
DELETE FROM albums WHERE AlbumId = @albumId
```

```csharp
using DbCommand command = cnn.DeleteAlbum(albumId: 12);
int affected = command.ExecuteNonQuery();
```

## Several result sets

```sql
SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId;
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId;
```

CodeGen records generated result information for the first result set. The command still contains the complete SQL.

```csharp
using DbCommand command = cnn.GetArtistAndAlbums(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
reader.NextResult();
```

## Database name that is not a C# name

```sql
SELECT AlbumId, Title AS [Album Title] FROM albums
```

```csharp
public partial record AlbumRow(int AlbumId, [TrueName("Album Title")] string Album_Title);
```

The generated support file defines `TrueNameAttribute` for the project.

## Nullable input

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
using DbCommand command = cnn.FindAlbums(title: null);
```

## PostgreSQL parameters

PostgreSQL native type metadata can be applied after the common `DbParameter` creation path.

```csharp
object payload = "{}";
var p_payload = command.Add("@payload", DbType.String, payload);
if (p_payload is not Npgsql.NpgsqlParameter npgsql_p_payload)
    throw new InvalidOperationException("PostgreSQL generated commands require Npgsql parameters.");
npgsql_p_payload.DataTypeName = "jsonb";
```

Native PostgreSQL type names are emitted when discovered metadata carries a type that common `DbType` metadata cannot represent completely.

Positional SQL keeps its placeholders. Named and positional parameter forms are not mixed in one generated query.

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

An unresolved SQLite parameter stays value inferred.

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

[Parameter corrections](queries.md#parameter-correction)

## Output parameter

```csharp
int counter = 0;
using DbCommand command = cnn.UpdateCounter(counter, out DbParameter out_counter);
command.ExecuteNonQuery();
object updatedCounter = out_counter.Value;
```

Pure output parameters expose only the `out DbParameter` argument. Input output parameters keep the input argument and expose the generated parameter through `out`.

## Output files

```text
.PowerTools.rinku.cs
Data/Generated/DbCommands.rinku.cs
```

The support file lives at the project root. The command file uses the configured output path.

[Configure](configure.md) · [Refresh](refresh.md)
