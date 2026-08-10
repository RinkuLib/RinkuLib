# Coming from Dapper

This page compares the core `Dapper` package and its `Dapper.SqlBuilder`
companion with Rinku. It is based on Dapper's public API and the upstream
[`tests/Dapper.Tests`](https://github.com/DapperLib/Dapper/tree/main/tests/Dapper.Tests)
cases. `Dapper.Rainbow` and the Entity Framework packages are separate
libraries and are outside this comparison.

RinkuLib began as a Dapper extension, so the patterns carry over. There are two calling styles, and both mirror a Dapper call.

- Hand the SQL to the connection. This reads almost like Dapper, and the command is built once and cached by the string.
- Declare a reusable `QueryCommand` and call methods on it. This is the primary Rinku form. The SQL is parsed once up front and each call skips the by-string lookup.

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>("SELECT * FROM albums WHERE ArtistId = @id", new { id = 1 });

// RinkuLib, SQL on the connection
List<Album> albums = cnn.Query<List<Album>>("SELECT * FROM albums WHERE ArtistId = @id", new { id = 1 });

// RinkuLib, reusable command
static readonly QueryCommand ByArtist = new("SELECT * FROM albums WHERE ArtistId = @id");
List<Album> albums = ByArtist.Query<List<Album>>(cnn, new { id = 1 });
```

## The shape is a type argument

Where Dapper picks the result shape with the method name, Rinku picks it with the `T` in `Query<T>`.

| Dapper | RinkuLib |
| --- | --- |
| `QueryFirst<T>` | `Query<T>` |
| `QueryFirst<T?>` for a nullable value type | `Query<T?>` |
| `QueryFirst<T?>` for a nullable reference type | `Query<MaybeNull<T>>` |
| `QueryFirstOrDefault<T?>` for a nullable reference type | `Query<OptionalNullable<T>>` |
| `QuerySingle<T>` | `Query<Single<T>>` |
| `Query<T>` (buffered) | `Query<List<T>>` |
| `Query<T>` (`buffered: false`) | `Query<IEnumerable<T>>` |
| `Execute` | `Execute` |
| `ExecuteScalar<T>` | `ExecuteScalar<T>` |
| `QueryMultiple` | `ExecuteMultiReader` |
| `Query<dynamic>` | `Query<DynaObject>` |

Each reads either way, `cnn.Query<List<T>>(sql, p)` or `cmd.Query<List<T>>(cnn, p)`. The result wrappers are on [result shapes](../running-queries/result-shapes.md).

Dapper's nullable `QueryFirstOrDefault` result collapses a missing row and a present database `NULL` into the same value. `OptionalNullable<T>` matches that behavior for a reference type. Rinku can keep the rules separate instead: `Optional<T>` and `OptionalStruct<T>` accept a missing row but still reject a present `NULL`, while `MaybeNull<T>` accepts `NULL` but still requires a row.

## Parameters

The anonymous-object habit carries over unchanged. Any object or struct with public readable fields or properties can supply values. Member names match variables case-insensitively, and unmatched members are ignored.

```csharp
// Dapper
cnn.Query<Album>("... WHERE ArtistId = @artistId", new { ArtistID = 1 });

// RinkuLib
cnn.Query<List<Album>>("... WHERE ArtistId = @artistId", new { ArtistID = 1 });
```

When C# logic should set the values instead of an object, a builder is the other road.

```csharp
var b = ByArtist.StartBuilder();
b.Use("@id", 1);
List<Album> albums = b.Query<List<Album>>(cnn);
```

The extra abilities (usage attributes, builders) are on [supplying values](../running-queries/parameters.md).

## Dapper capabilities in Rinku

This is a capability comparison, not a list of matching method names. Rinku moves
some choices from the call to the cached command, the result type, registration,
or a composable handler.

| Dapper operation | Rinku expression |
| --- | --- |
| `Execute(sql, parameters)` | `QueryCommand.Execute` or `QueryBuilder.Execute` |
| `Execute(sql, IEnumerable<T>)` | one `QueryBuilderCommand` and `UseWith` for each item |
| `ExecuteReader` for a `DataTable` or `DataSet` | `ExecuteReader`, then use the returned reader directly |
| stored procedure execution | `CommandType.StoredProcedure` or `QueryCommand.FromProc` |
| output and return-value parameters | `FromProc` metadata or directional `DbParamInfo`, then `GetOutputValue<T>` / `GetReturnValue<T>` on the handed-back command |
| `DynamicParameters` | `QueryBuilder`, a registered parameter object, or `DbParamInfo` |
| `SqlBuilder` | conditional SQL and handlers |
| `QueryMultiple` | `ExecuteMultiReader` and `MultiReader` |
| `GetRowParser<T>` | `TypeParser.GetTypeParser<T>` and type registration |
| per-row type switching | `GetCurrentSetParser<T>` or a custom parser selected by the caller |
| multi-map with `splitOn` | registered nested types, tuples, or a custom construction path |
| custom result type handler | a registered `TypeParsingInfo`, commonly based on `ScalarTypeParsingInfo<T>` |
| custom parameter type handler | `ConvertedDbParamInfo<T>` or a custom `DbParamInfo` |
| `DbString` | a `DbParamInfo` that sets provider type, size, and encoding |
| table-valued parameter | a provider-specific `DbParamInfo` |
| literal replacement (`{=value}`) | query handlers such as `_N`, `_S`, and `_R` |
| `dynamic` result | `DynaObject` |
| buffered and unbuffered queries | `List<T>`, `IEnumerable<T>`, and `StreamQueryAsync<T>` |
| async, cancellation, transactions, and timeout | the async, cancellation, transaction, and timeout overloads |

For the upstream core operations and test cases, Rinku has an expression of
the same underlying capability. The APIs are not identical, and Rinku's null
and registration rules remain its own rules.

The batch form is not a missing capability. It is a small wrapper around a
reusable command, and the explicit loop leaves the caller in control of each
item:

```csharp
var update = new QueryCommand("UPDATE tracks SET Name = @name WHERE Id = @id");
using var command = cnn.CreateCommand();
var batch = update.StartBuilder(command);

foreach (var item in items) {
    batch.UseWith(item);       // conditions, handlers, and custom DbParamInfo still apply
    batch.Execute();           // the same DbCommand is reused
}
```

The equivalent Dapper call chooses the parameter object and performs the loop
inside Dapper:

```csharp
cnn.Execute("UPDATE tracks SET Name = @name WHERE Id = @id", items);
```

The difference is the location of the loop, not the database operation or the
mapping capability.

For example, a multiple-result query and a dynamic result use the same cached
command and reader infrastructure:

```csharp
var builder = Search.StartBuilder();
builder.Use("@artistId", 1);
builder.Use("WithTracks");

using var multi = builder.ExecuteMultiReader(cnn);
List<Album> albums = multi.Query<List<Album>>();
List<Track> tracks = multi.Query<List<Track>>();

// A Dapper dynamic row is read as Rinku's dynamic shape.
List<DynaObject> rows = Search.Query<List<DynaObject>>(cnn, new { artistId = 1 });
```

Dapper provider cases can be reproduced through Rinku's provider-neutral entrypoints. Rinku does not pretend
to be a provider implementation. The provider adapter supplies the provider-specific operation, while Rinku
keeps the command and mapping pipeline generic:

```csharp
// The adapter registers an ordinary TypeParsingInfo for int[]. Its DbItemPlan emits
// reader.GetFieldValue<int[]>(ordinal) directly into the generated parser.
TypeParsingInfo.AddOrSet(typeof(int[]), new PostgresIntArrayInfo());
```

The adapter can also take complete control of a provider parameter through `DbParamInfo`. Other provider
seams include `IDbParamInfoGetter.ParamGetterMakers` for reading provider-resolved parameter metadata and
`StoredProcedure.ParameterDeriver` for provider procedure metadata. A Dapper test that exercises provider
behavior uses the same SQL and result shape, then installs the provider behavior through one of these seams.
The provider is external to Rinku, but the capability remains available to the application.

Positional parameters follow the same rule. Rinku does not rewrite named SQL into `?` placeholders, but a
caller can provide positional SQL and use the built-in `PositionalDbParamInfo` to create the provider parameters
in order. The slot names are only internal identifiers and are not sent to the provider:

```csharp
using System.Data;
using Rinku.Querying.Defaults;

var query = new QueryCommand("UPDATE tracks SET Name = ? WHERE Id = ?", ["param0", "param1"], CommandType.Text);

query.UpdateParamCache(0, new PositionalDbParamInfo());
query.UpdateParamCache(1, new PositionalDbParamInfo());
var positional = query.StartBuilder();
positional.Use(0, "Live");
positional.Use(1, 7);
positional.Execute(cnn);
```

The SQL remains provider-specific, but the registration and execution path remain under the caller's control.

Dapper multi-map uses `splitOn` because its mapping is chosen for one run. Rinku registers the nested types
once, then negotiation finds their columns from the type and name rules:

```csharp
public class RegisteredChild {
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
public record RegisteredParent(int Id, RegisteredChild Child) : IDbReadable;

_ = TypeParsingInfo.GetOrAdd<RegisteredChild>();
static readonly QueryCommand Query = new("SELECT Id, ChildId, ChildName FROM rows");
RegisteredParent row = Query.Query<RegisteredParent>(cnn);
// Id | ChildId | ChildName -> RegisteredParent(Id, RegisteredChild(Id, Name))
```

There is no per-run split string. A user-controlled `TypeParsingInfo` or construction path can replace the
default negotiation when the normal names are not enough.

## Differences to keep in mind

Rinku keeps its own null rule. A null column is accepted by a nullable slot, and a non-nullable slot
raises `NullValueAssignmentException`. Dapper can leave a constructor or member default in this case when
`ApplyNullValues` is off. The Rinku tests assert the Rinku rule instead of copying that setting.

The same rule applies to a custom value type. Give the type a construction path or register one, then use
the normal parameter path when writing it:

```csharp
public readonly record struct LocalDate(DateTime Value) : IDbReadable;

public sealed record Invoice(LocalDate Date) : IDbReadable;

// Read: the normal construction path can already use LocalDate(DateTime).
// When reading needs different behavior, register a ScalarTypeParsingInfo<LocalDate>.

// For a provider-specific or multi-step binding rule, implement DbParamInfo directly.
sealed class LocalDateParam : DbParamInfo
{
    public LocalDateParam() : base(true) { }

    public override bool SaveUse(string name, IDbCommand cmd, ref object value)
    {
        var p = Add(name, cmd, (LocalDate)value);
        value = p;
        return true;
    }

    public override bool Use(string name, IDbCommand cmd, object value)
    {
        Add(name, cmd, (LocalDate)value);
        return true;
    }

    public override bool Use(string name, DbCommand cmd, object value)
    {
        Add(name, cmd, (LocalDate)value);
        return true;
    }

    private static IDbDataParameter Add(string name, IDbCommand cmd, LocalDate value)
    {
        var p = (IDbDataParameter)cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.DateTime;
        p.Value = value.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    public override bool Update(IDbCommand cmd, ref object current, object newValue)
    {
        ((IDbDataParameter)current).Value = ((LocalDate)newValue).Value;
        return true;
    }

    public override void Remove(IDbCommand cmd, object current)
        => DbParamInfo.RemoveSingle(((IDbDataParameter)current).ParameterName, cmd);
}
```

Result-side customization remains an ordinary `TypeParsingInfo` registration. `ScalarTypeParsingInfo<T>` and
`ScalarDbItemPlan<T>` remove the single-column boilerplate without introducing another registration system.
`DbParamInfo` remains the corresponding parameter-side takeover.

## IN clauses

Dapper expands a collection parameter automatically. Rinku does it with the explicit `_X` suffix on the variable.

```csharp
// Dapper
cnn.Query<Track>("SELECT * FROM tracks WHERE GenreId IN @genreIds", new { genreIds = new[] { 1, 2, 3 } });

// RinkuLib
cnn.Query<List<Track>>("SELECT * FROM tracks WHERE GenreId IN (@genreIds_X)", new { genreIds = new[] { 1, 2, 3 } });
// GenreId IN (@genreIds_1, @genreIds_2, @genreIds_3)
```

## What replaces string-built SQL

Where a Dapper codebase concatenates SQL or leans on `WHERE 1=1`, one [conditional template](../conditional-sql/index.md) covers the variations. Mark the optional parts and the values you pass decide the SQL.

```csharp
static readonly QueryCommand Search = new("SELECT * FROM tracks WHERE AlbumId = ?@albumId AND GenreId IN (?@genreIds_X)");

Search.Query<List<Track>>(cnn, new { albumId = 1 });
// SELECT * FROM tracks WHERE AlbumId = @albumId
```
