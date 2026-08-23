# Coming from Dapper

[SQL strings](../running-queries/sql-string.md) · [Result shapes](../running-queries/result-shapes.md)

## Query<T>

```csharp
public record Album(int Id, string Title);
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";
var parameters = new { artistId = 7 };
```

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>(sql, parameters);
```

```csharp
// Rinku
List<Album> albums = cnn.Query<List<Album>>(sql, parameters);

Album first = cnn.Query<Album>(sql, parameters);
Album[] array = cnn.Query<Album[]>(sql, parameters);
IEnumerable<Album> streamed = cnn.Query<IEnumerable<Album>>(sql, parameters);
```

[Result shapes](../running-queries/result-shapes.md) · [Object mapping](../mapping/objects.md)

## Runtime result Type

```csharp
Type albumType = typeof(Album);
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";
var parameters = new { artistId = 7 };
```

```csharp
// Dapper
IEnumerable<object> albums = cnn.Query(albumType, sql, parameters);
object album = cnn.QuerySingle(albumType, sql, parameters);
```

```csharp
// Rinku
Type listType = typeof(List<>).MakeGenericType(albumType);
Type singleType = typeof(Single<>).MakeGenericType(albumType);

IEnumerable<object> albums = (IEnumerable<object>)cnn.Query(listType, sql, parameters);
object album = cnn.Query(singleType, sql, parameters);
```

[Runtime result types](../running-queries/result-shapes.md#runtime-result-type)

## QueryFirst<T>

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
Album album = cnn.QueryFirst<Album>(sql, parameters);
```

```csharp
// Rinku
Album album = cnn.Query<Album>(sql, parameters);
```

[First complete result](../running-queries/result-shapes.md)

## QueryFirstOrDefault<T>

```csharp
const string albumSql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
const string yearSql = "SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
Album? album = cnn.QueryFirstOrDefault<Album>(albumSql, parameters);
int year = cnn.QueryFirstOrDefault<int>(yearSql, parameters);
int? nullableYear = cnn.QueryFirstOrDefault<int?>(yearSql, parameters);
```

```csharp
// Rinku, no row accepted, mapped database NULL rejected.
Album? album = cnn.Query<Optional<Album>>(albumSql, parameters);
int? year = cnn.Query<OptionalStruct<int>>(yearSql, parameters);
```

```csharp
// Rinku, no row and mapped database NULL accepted.
Album? album = cnn.Query<OptionalNullable<Album>>(albumSql, parameters);
int? nullableYear = cnn.Query<OptionalNullableStruct<int>>(yearSql, parameters);
```

[Optional result shapes](../running-queries/result-shapes.md) · [Database NULL](../mapping/nulls.md)

## QuerySingle<T>

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
Album album = cnn.QuerySingle<Album>(sql, parameters);
```

```csharp
// Rinku
Album album = cnn.Query<Single<Album>>(sql, parameters);
```

[Single result shapes](../running-queries/result-shapes.md)

## QuerySingleOrDefault<T>

```csharp
const string albumSql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
const string yearSql = "SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
Album? album = cnn.QuerySingleOrDefault<Album>(albumSql, parameters);
int year = cnn.QuerySingleOrDefault<int>(yearSql, parameters);
int? nullableYear = cnn.QuerySingleOrDefault<int?>(yearSql, parameters);
```

```csharp
// Rinku, no row accepted, mapped database NULL rejected.
Album? album = cnn.Query<SingleOrDefault<Album>>(albumSql, parameters);
int? year = cnn.Query<SingleOrDefaultStruct<int>>(yearSql, parameters);
```

```csharp
// Rinku, no row and mapped database NULL accepted.
Album? album = cnn.Query<SingleOrDefaultNullable<Album>>(albumSql, parameters);
int? nullableYear = cnn.Query<SingleOrDefaultNullableStruct<int>>(yearSql, parameters);
```

[Single result shapes](../running-queries/result-shapes.md) · [Database NULL](../mapping/nulls.md)

## Buffered and unbuffered queries

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId";
```

```csharp
// Dapper
IEnumerable<Album> buffered = cnn.Query<Album>(sql);
IEnumerable<Album> streamed = cnn.Query<Album>(sql, buffered: false);
```

```csharp
// Rinku
List<Album> buffered = cnn.Query<List<Album>>(sql);
IEnumerable<Album> streamed = cnn.Query<IEnumerable<Album>>(sql);
```

[Result shapes](../running-queries/result-shapes.md) · [Streaming](../running-queries/streaming.md)

### Async buffered query

```csharp
// Dapper
IEnumerable<Album> albums = await cnn.QueryAsync<Album>(sql);
```

```csharp
// Rinku
List<Album> albums = await cnn.QueryAsync<List<Album>>(sql, ct: cancellationToken);
```

[Async execution](../running-queries/async.md)

### Async streaming

```csharp
// Dapper
await foreach (Album album in cnn.QueryUnbufferedAsync<Album>(sql).WithCancellation(cancellationToken))
    Console.WriteLine(album.Title);
```

```csharp
// Rinku
await foreach (Album album in cnn.StreamQueryAsync<Album>(sql, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

[Async streaming](../running-queries/streaming.md)

## Execute

```csharp
const string sql = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
var parameters = new { albumId = 12, title = "Kind of Blue" };
```

```csharp
// Dapper
int affected = cnn.Execute(sql, parameters);
```

```csharp
// Rinku
int affected = cnn.Execute(sql, parameters);
```

[Execution](../running-queries/execution.md)

## Execute a sequence

```csharp
public record AlbumUpdate(int Id, string Title);

AlbumUpdate[] albums = [new(1, "Blue"), new(2, "Green")];
const string sql = "UPDATE albums SET Title = @Title WHERE AlbumId = @Id";
```

```csharp
// Dapper
int affected = cnn.Execute(sql, albums);
```

```csharp
// Rinku
QueryCommand updateAlbum = new(sql);
using DbCommand command = cnn.CreateCommand();
var batch = updateAlbum.StartBuilder(command);

int affected = 0;
foreach (AlbumUpdate album in albums)
{
    batch.UseWith(album);
    affected += batch.Execute();
}
```

[Builders](../running-queries/builders.md) · [Execution](../running-queries/execution.md)

## ExecuteScalar<T>

```csharp
const string sql = "INSERT INTO albums (ArtistId, Title) VALUES (@artistId, @title); SELECT CAST(SCOPE_IDENTITY() AS int);";
var parameters = new { artistId = 7, title = "Blue" };
```

```csharp
// Dapper
int albumId = cnn.ExecuteScalar<int>(sql, parameters);
```

```csharp
// Rinku
int albumId = cnn.ExecuteScalar<int>(sql, parameters);
```

[Scalar execution](../running-queries/execution.md)

## Parameters

### Anonymous objects and POCOs

```csharp
public record AlbumSearch(int ArtistId, string Title);

const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @ArtistId AND Title = @Title";
var parameters = new AlbumSearch(7, "Blue");
```

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>(sql, parameters);
```

```csharp
// Rinku
List<Album> albums = cnn.Query<List<Album>>(sql, parameters);
```

[Supplying values](../running-queries/values.md)

### Dictionaries

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";
var parameters = new Dictionary<string, object> { ["artistId"] = 7 };
```

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>(sql, parameters);
```

```csharp
// Rinku
List<Album> albums = cnn.Query<List<Album>>(sql, parameters);
```

[Dictionary values](../running-queries/values.md)

### DynamicParameters and several parameter sources

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title = @title";
```

```csharp
// Dapper
var parameters = new DynamicParameters(new { artistId = 7 });
parameters.AddDynamicParams(new { title = "Blue" });
IEnumerable<Album> albums = cnn.Query<Album>(sql, parameters);
```

```csharp
// Rinku
QueryCommand searchAlbums = new(sql);
var builder = searchAlbums.StartBuilder();
builder.UseWith(new { artistId = 7 });
builder.UseWith(new { title = "Blue" });
List<Album> albums = builder.Query<List<Album>>(cnn);
```

[Builders](../running-queries/builders.md)

### Explicit parameter metadata

```csharp
// Dapper
const string sql = "UPDATE albums SET Price = @price WHERE AlbumId = @albumId";
DynamicParameters parameters = new();
parameters.Add("price", 12.50m, DbType.Decimal, precision: 18, scale: 2);
parameters.Add("albumId", 12);
cnn.Execute(sql, parameters);
```

```csharp
// Rinku
static readonly QueryCommand UpdateAlbumPrice = CreateUpdateAlbumPrice();

static QueryCommand CreateUpdateAlbumPrice()
{
    QueryCommand command = new("UPDATE albums SET Price = @price WHERE AlbumId = @albumId");
    command.UpdateParamCache("@price", new ScaledDbParamCache(DbType.Decimal, 18, 2));
    return command;
}

UpdateAlbumPrice.Execute(cnn, new { albumId = 12, price = 12.50m });
```

[Parameter metadata](../running-queries/parameter-metadata.md)

### DbString

```csharp
// Dapper
const string sql = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
var title = new DbString { Value = "Blue", IsAnsi = true, Length = 100 };
cnn.Execute(sql, new { albumId = 12, title });
```

```csharp
// Rinku
static readonly QueryCommand UpdateAlbum = CreateUpdateAlbum();

static QueryCommand CreateUpdateAlbum()
{
    QueryCommand command = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
    command.UpdateParamCache("@title", TypedDbParamCache.Get(DbType.AnsiString, 100));
    return command;
}

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Blue" });
```

[Parameter metadata](../running-queries/parameter-metadata.md)

### null and database NULL

```csharp
const string sql = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
```

```csharp
// Dapper
cnn.Execute(sql, new { albumId = 12, title = (string?)null });
```

```csharp
// Rinku
// null means that the value is absent.
// DBNull.Value sends database NULL.
cnn.Execute(sql, new { albumId = 12, title = DBNull.Value });
```

```csharp
// Rinku, nullable application member mapped to database NULL.
public record AlbumTitleUpdate(int AlbumId, [property: UseDbNull] string? Title);

cnn.Execute("UPDATE albums SET Title = @Title WHERE AlbumId = @AlbumId", new AlbumTitleUpdate(12, null));
```

[Parameter values](../running-queries/values.md) · [Parameter members](../customization/parameter-members.md)

## IN and collection expansion

```csharp
int[] albumIds = [2, 5];
```

```csharp
// Dapper
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN @albumIds";
IEnumerable<Album> albums = cnn.Query<Album>(sql, new { albumIds });
```

```csharp
// Rinku
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@albumIds_X)";
List<Album> albums = cnn.Query<List<Album>>(sql, new { albumIds });
// @albumIds_X becomes @albumIds_0, @albumIds_1.
```

[Collection expansion](../conditional-sql/collections.md)

## Literal replacement

```csharp
// Dapper
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE Status = {=status}";
IEnumerable<Album> albums = cnn.Query<Album>(sql, new { status = 1 });
```

```csharp
// Rinku
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE Status = @status_N";
List<Album> albums = cnn.Query<List<Album>>(sql, new { status = 1 });
```

[Conditional SQL handlers](../conditional-sql/handlers.md)

## Output parameters

```csharp
// Dapper
var parameters = new DynamicParameters();
parameters.Add("albumId", 12);
parameters.Add("moved", dbType: DbType.Int32, direction: ParameterDirection.Output);
cnn.Execute("RenumberAlbums", parameters, commandType: CommandType.StoredProcedure);
int moved = parameters.Get<int>("moved");
```

```csharp
// Rinku
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", cnn);
renumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command)
{
    int moved = command.GetOutputValue<int>("@moved");
}
```

[Stored procedure output values](../running-queries/stored-procedures.md)

## Dynamic rows

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
dynamic album = cnn.QuerySingle(sql, parameters);
string title = album.Title;
IDictionary<string, object> values = (IDictionary<string, object>)album;
```

```csharp
// Rinku
DynaObject album = cnn.Query<DynaObject>(sql, parameters);
string title = album.Get<string>("Title");
IReadOnlyDictionary<string, object?> values = album;
```

[Dynamic rows](../mapping/dynamic-rows.md)

## Constructor mapping

```csharp
public sealed class Customer
{
    public int Id { get; }
    public string Name { get; }

    public Customer(int customerId, string displayName)
    {
        Id = customerId;
        Name = displayName;
    }
}

const string sql = "SELECT CustomerId, DisplayName FROM customers";
```

```csharp
// Dapper
IEnumerable<Customer> customers = cnn.Query<Customer>(sql);
```

```csharp
// Rinku
List<Customer> customers = cnn.Query<List<Customer>>(sql);
```

[Construction paths](../mapping/construction-paths.md)

## Custom column names

```csharp
const string sql = "SELECT customer_id, display_name FROM customers";
```

```csharp
// Dapper
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

SqlMapper.SetTypeMap(typeof(Customer), new CustomPropertyTypeMap(typeof(Customer), (type, column) => column switch
{
    "customer_id" => type.GetProperty(nameof(Customer.Id)),
    "display_name" => type.GetProperty(nameof(Customer.Name)),
    _ => null
}));

IEnumerable<Customer> customers = cnn.Query<Customer>(sql);
```

```csharp
// Rinku, mapping on the type.
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);
List<Customer> customers = cnn.Query<List<Customer>>(sql);
```

```csharp
// Rinku, mapping outside the type.
public record ExternalCustomer(int Id, string Name);

TypeParsingInfo.GetOrAdd<ExternalCustomer>().UpdateAltName(names => names.GetDefaultName() switch
{
    "Id" => new NameComparer("customer_id"),
    "Name" => new NameComparer("display_name"),
    _ => null
});

List<ExternalCustomer> customers = cnn.Query<List<ExternalCustomer>>(sql);
```

[Name adaptation](../mapping/names.md)

## MatchNamesWithUnderscores

```csharp
const string sql = "SELECT customer_id, display_name FROM customers";
```

```csharp
// Dapper
public record CustomerByConvention(int CustomerId, string DisplayName);
DefaultTypeMap.MatchNamesWithUnderscores = true;
IEnumerable<CustomerByConvention> customers = cnn.Query<CustomerByConvention>(sql);
```

```csharp
// Rinku
public record CustomerByConvention([Alt("customer_id")] int CustomerId, [Alt("display_name")] string DisplayName);
List<CustomerByConvention> customers = cnn.Query<List<CustomerByConvention>>(sql);
```

[Name adaptation](../mapping/names.md)

## Multi mapping and nested objects

```csharp
// Dapper
public record User(int Id, string Name);

public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public User? Owner { get; set; }
}

const string sql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";
IEnumerable<Post> posts = cnn.Query<Post, User, Post>(sql, (post, owner) =>
{
    post.Owner = owner;
    return post;
}, splitOn: "Id");
```

The same Rinku mapping can be expressed at different parts of the boundary.

```csharp
// Rinku, SQL names the nested path.
public record User(int Id, string Name) : IDbReadable;
public record Post(int Id, string Title, User Owner);

const string sql = "SELECT p.Id, p.Title, u.Id AS OwnerId, u.Name AS OwnerName FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";
List<Post> posts = cnn.Query<List<Post>>(sql);
```

```csharp
// Rinku, the nested member starts at the next columns.
public record User(int Id, string Name) : IDbReadable;
public record Post(int Id, string Title, [NoName] User Owner);

const string sql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";
List<Post> posts = cnn.Query<List<Post>>(sql);
```

```csharp
// Rinku, the same name rule configured outside the types.
public record User(int Id, string Name);
public record Post(int Id, string Title, User Owner);

TypeParsingInfo.GetOrAdd<User>();
TypeParsingInfo.GetOrAdd<Post>().UpdateAltName(names => names.GetDefaultName() == "Owner" ? NoNameComparer.Instance : null);

const string sql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";
List<Post> posts = cnn.Query<List<Post>>(sql);
```

[Nested mapping](../mapping/nesting.md) · [Name adaptation](../mapping/names.md)

## One to many mapping

```csharp
// Dapper
public record Album(int Id, string Title);

public sealed class ArtistWithAlbums
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Album> Albums { get; set; } = [];
}

const string sql = "SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS Id, al.Title FROM artists ar INNER JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId";
List<ArtistWithAlbums> artists = [];
ArtistWithAlbums? current = null;

cnn.Query<ArtistWithAlbums, Album, ArtistWithAlbums>(sql, (artist, album) =>
{
    if (current is null || current.Id != artist.Id)
    {
        current = artist;
        artists.Add(current);
    }

    current.Albums.Add(album);
    return current;
}, splitOn: "Id");
```

```csharp
// Rinku
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, [Alt("Album")] List<Album> Albums);

const string sql = "SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumId, al.Title AS AlbumTitle FROM artists ar INNER JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId";
List<ArtistWithAlbums> artists = cnn.Query<List<ArtistWithAlbums>>(sql);
```

The `List<Album>` member is a multi-row mapping. `Album` is still mapped through the same recursive type mapping used elsewhere.

[Multi-row mapping](../mapping/collections.md) · [Grouping](../mapping/grouping.md)

## GetRowParser and runtime concrete types

```csharp
const string sql = "SELECT Type, Radius, Width, Height FROM Shapes";
```

```csharp
// Dapper
public interface IShape { }
public sealed class Circle : IShape { public double Radius { get; set; } }
public sealed class Rectangle : IShape { public double Width { get; set; } public double Height { get; set; } }

using IDataReader reader = cnn.ExecuteReader(sql);
Func<IDataReader, IShape> circleParser = reader.GetRowParser<IShape>(typeof(Circle));
Func<IDataReader, IShape> rectangleParser = reader.GetRowParser<IShape>(typeof(Rectangle));
int typeOrdinal = reader.GetOrdinal("Type");
List<IShape> shapes = [];

while (reader.Read())
{
    shapes.Add(reader.GetString(typeOrdinal) switch
    {
        "Circle" => circleParser(reader),
        "Rectangle" => rectangleParser(reader),
        _ => throw new InvalidOperationException()
    });
}
```

```csharp
// Rinku
public interface IShape : IDbReadable
{
    public static IShape Create(string type, double? radius, double? width, double? height) => type switch
    {
        "Circle" => new Circle { Radius = radius ?? throw new InvalidOperationException() },
        "Rectangle" => new Rectangle { Width = width ?? throw new InvalidOperationException(), Height = height ?? throw new InvalidOperationException() },
        _ => throw new InvalidOperationException()
    };
}

public sealed class Circle : IShape { public double Radius { get; set; } }
public sealed class Rectangle : IShape { public double Width { get; set; } public double Height { get; set; } }

List<IShape> shapes = cnn.Query<List<IShape>>(sql);
```

[Construction paths](../mapping/construction-paths.md)

## TypeHandler<T>

```csharp
// Dapper
public readonly record struct Money(decimal Value);

public sealed class MoneyHandler : SqlMapper.TypeHandler<Money>
{
    public override Money Parse(object value) => new(Convert.ToDecimal(value));

    public override void SetValue(IDbDataParameter parameter, Money value)
    {
        parameter.DbType = DbType.Decimal;
        parameter.Value = value.Value;
    }
}

SqlMapper.AddTypeHandler(new MoneyHandler());
```

```csharp
// Rinku read.
public readonly record struct Money([NoName] decimal Value) : IDbReadable;
public record Invoice(int Id, Money Total);

List<Invoice> invoices = cnn.Query<List<Invoice>>("SELECT InvoiceId AS Id, Total FROM invoices");
```

```csharp
// Rinku write.
sealed class MoneyParamInfo : ConvertedDbParamInfo<Money>
{
    protected override object ConvertValue(Money value) => value.Value;
    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.Decimal;
}

static readonly QueryCommand SaveInvoice = CreateSaveInvoice();

static QueryCommand CreateSaveInvoice()
{
    QueryCommand command = new("INSERT INTO invoices (Total) VALUES (@total)");
    command.UpdateParamCache("@total", new MoneyParamInfo());
    return command;
}

SaveInvoice.Execute(cnn, new { total = new Money(12.50m) });
```

[Type registration](../customization/type-registration.md) · [Parameter binding](../customization/parameters.md)

## ICustomQueryParameter and TVP

```csharp
DataTable ids = new();
ids.Columns.Add("Id", typeof(int));
ids.Rows.Add(1);
ids.Rows.Add(2);
ids.Rows.Add(3);

const string sql = "SELECT a.AlbumId AS Id, a.Title FROM albums a INNER JOIN @ids i ON i.Id = a.AlbumId";
```

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>(sql, new { ids = ids.AsTableValuedParameter("dbo.IntIds") });
```

```csharp
// Rinku
sealed class SqlServerTableParamInfo(string typeName) : ConvertedDbParamInfo<DataTable>
{
    protected override object ConvertValue(DataTable value) => value;

    protected override void ConfigureParameter(IDbDataParameter parameter)
    {
        SqlParameter sqlParameter = (SqlParameter)parameter;
        sqlParameter.SqlDbType = SqlDbType.Structured;
        sqlParameter.TypeName = typeName;
    }
}

static readonly QueryCommand AlbumsByIds = CreateAlbumsByIds();

static QueryCommand CreateAlbumsByIds()
{
    QueryCommand command = new("SELECT a.AlbumId AS Id, a.Title FROM albums a INNER JOIN @ids i ON i.Id = a.AlbumId");
    command.UpdateParamCache("@ids", new SqlServerTableParamInfo("dbo.IntIds"));
    return command;
}

List<Album> albums = AlbumsByIds.Query<List<Album>>(cnn, new { ids });
```

[Parameter binding](../customization/parameters.md)

## QueryMultiple and GridReader

```csharp
const string sql = "SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId";
var parameters = new { artistId = 7 };
```

```csharp
// Dapper
using var results = cnn.QueryMultiple(sql, parameters);
Artist artist = results.ReadSingle<Artist>();
List<Album> albums = results.Read<Album>().AsList();
```

```csharp
// Rinku
using MultiReader results = cnn.ExecuteMultiReader(sql, parameters);
Artist artist = results.Query<Single<Artist>>();
List<Album> albums = results.Query<List<Album>>();
```

```csharp
// Rinku runtime result type.
Type artistType = typeof(Artist);
Type listType = typeof(List<>).MakeGenericType(artistType);
using MultiReader results = cnn.ExecuteMultiReader(sql, parameters);
IEnumerable<object> artists = (IEnumerable<object>)results.Query(listType);
```

[Multiple result sets](../running-queries/multiple-results.md)

## Stored procedures

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>("GetAlbumsForArtist", new { artistId = 7 }, commandType: CommandType.StoredProcedure);
```

```csharp
// Rinku, parameter names declared with the command.
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);
List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

```csharp
// Rinku, parameter metadata discovered from the provider.
QueryCommand getAlbumsForArtist = QueryCommand.FromProc("GetAlbumsForArtist", cnn);
List<Album> albums = getAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

[Stored procedures](../running-queries/stored-procedures.md)

## Transactions, timeouts, command type, and cancellation

```csharp
// Dapper
CommandDefinition command = new(sql, parameters, transaction, commandTimeout: 30, commandType: CommandType.Text, cancellationToken: cancellationToken);
IEnumerable<Album> albums = await cnn.QueryAsync<Album>(command);
```

```csharp
// Rinku
List<Album> albums = await cnn.QueryAsync<List<Album>>(sql, parameters, transaction: transaction, timeout: 30, ct: cancellationToken);
```

[Execution context](../running-queries/execution-context.md) · [Async](../running-queries/async.md)

## Raw DbDataReader

```csharp
// Dapper
using IDataReader reader = cnn.ExecuteReader(sql, parameters);
```

```csharp
// Rinku
DbDataReader reader = cnn.ExecuteReader(sql, out DbCommand command, parameters);

using (command)
using (reader)
{
    while (reader.Read())
    {
        int id = reader.GetInt32(0);
        string title = reader.GetString(1);
    }
}
```

[Raw readers](../running-queries/readers.md)

## Query cache and CommandFlags.NoCache

```csharp
// Dapper
CommandDefinition command = new(sql, parameters, flags: CommandFlags.NoCache);
IEnumerable<Album> albums = cnn.Query<Album>(command);
```

```csharp
// Rinku
QueryCommand getAlbums = ConnectionQueryExtensions.GetOrCreateCommand(sql);
List<Album> albums = getAlbums.Query<List<Album>>(cnn, parameters);
getAlbums.InvalidateParsers();
```

`CommandFlags.NoCache` and `InvalidateParsers()` do not perform the same operation. The Rinku call removes learned result parsers after the execution. The SQL string still resolves through its cached `QueryCommand`.

[SQL string cache](../running-queries/sql-string.md) · [Cache control](../customization/caches.md)

## Dapper.SqlBuilder

```csharp
// Dapper
SqlBuilder builder = new();
SqlBuilder.Template template = builder.AddTemplate("SELECT AlbumId AS Id, Title FROM albums /**where**/");
builder.Where("ArtistId = @artistId", new { artistId = 7 });
IEnumerable<Album> albums = cnn.Query<Album>(template.RawSql, template.Parameters);
```

```csharp
// Rinku, the optional SQL is part of the command template.
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title";
List<Album> albums = cnn.Query<List<Album>>(sql, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId
```

```csharp
// Rinku, values can also be assembled over several steps.
QueryCommand searchAlbums = new(sql);
var builder = searchAlbums.StartBuilder();
builder.Use("@artistId", 7);
builder.Use("@title", "Blue%");
List<Album> albums = builder.Query<List<Album>>(cnn);
```

[Conditional SQL](../conditional-sql/cheatsheet.md) · [Builders](../running-queries/builders.md)
