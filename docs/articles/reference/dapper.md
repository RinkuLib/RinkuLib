# Coming from Dapper

Comparisons use the [SQL string shortcut](../running-queries/sql-string.md) to stay direct, which simply redirects to a `QueryCommand`. Differences are called out when the two APIs do not have identical semantics.

## Query<T>
[Result shapes](../running-queries/result-shapes.md) and [Map rows to objects](../mapping/objects.md)

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
```
```csharp
// Result type selects the shape
Album first = cnn.Query<Album>(sql, parameters);
Album[] array = cnn.Query<Album[]>(sql, parameters);
List<Album> buffered = cnn.Query<List<Album>>(sql, parameters);
IEnumerable<Album> streamed = cnn.Query<IEnumerable<Album>>(sql, parameters);
```


## Runtime result Type
Compare runtime type queries with the [result shape rules](../running-queries/result-shapes.md) and [complete result parsers](../customization/result-parsers.md).

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


## QueryFirst<T>
Use the [result shape rules](../running-queries/result-shapes.md) to choose the corresponding Rinku result type.

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


## QueryFirstOrDefault<T>
Use the [result shape rules](../running-queries/result-shapes.md) and [database NULL guidance](../mapping/nulls.md) when choosing the equivalent Rinku shape.

```csharp
const string albumSql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
const string yearSql = "SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper

// class
Album? album = cnn.QueryFirstOrDefault<Album>(albumSql, parameters);

// struct
int year = cnn.QueryFirstOrDefault<int>(yearSql, parameters);
int? nullableYear = cnn.QueryFirstOrDefault<int?>(yearSql, parameters);
```

```csharp
// Rinku, mapped NULL rejected

// class
Album? album = cnn.Query<Optional<Album>>(albumSql, parameters);

// struct
int? year = cnn.Query<OptionalStruct<int>>(yearSql, parameters); // int? instead of int
```

```csharp
// Rinku, mapped NULL accepted

// class
Album? album = cnn.Query<OptionalNullable<Album>>(albumSql, parameters);

// struct
int? nullableYear = cnn.Query<OptionalNullableStruct<int>>(yearSql, parameters);
```


## QuerySingle<T>
Use the [result shape rules](../running-queries/result-shapes.md) to choose the corresponding Rinku result type.

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


## QuerySingleOrDefault<T>
[Result shapes](../running-queries/result-shapes.md) and [Database NULL](../mapping/nulls.md)

```csharp
const string albumSql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
const string yearSql = "SELECT ReleaseYear FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper

// class
Album? album = cnn.QuerySingleOrDefault<Album>(albumSql, parameters);

// struct
int year = cnn.QuerySingleOrDefault<int>(yearSql, parameters);
int? nullableYear = cnn.QuerySingleOrDefault<int?>(yearSql, parameters);
```

```csharp
// Rinku, mapped NULL rejected

// class
Album? album = cnn.Query<SingleOrDefault<Album>>(albumSql, parameters);

// struct
int? year = cnn.Query<SingleOrDefaultStruct<int>>(yearSql, parameters); // int? instead of int
```

```csharp
// Rinku, mapped NULL accepted

// class
Album? album = cnn.Query<SingleOrDefaultNullable<Album>>(albumSql, parameters);

// struct
int? nullableYear = cnn.Query<SingleOrDefaultNullableStruct<int>>(yearSql, parameters);
```


## Buffered and unbuffered queries
[Result shapes](../running-queries/result-shapes.md) and [Streaming](../running-queries/streaming.md)

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

### Async buffered query
[Async execution](../running-queries/async.md) and [Result shapes](../running-queries/result-shapes.md)

```csharp
// Dapper
IEnumerable<Album> albums = await cnn.QueryAsync<Album>(sql);
```
```csharp
// Rinku
List<Album> albums = await cnn.QueryAsync<List<Album>>(sql, ct: cancellationToken);
```


### Async streaming
[Async execution](../running-queries/async.md) and [Streaming](../running-queries/streaming.md)

```csharp
// Dapper
int count = 0;
await foreach (Album album in cnn.QueryUnbufferedAsync<Album>(sql).WithCancellation(cancellationToken))
    count++;
```

```csharp
// Rinku
int count = 0;
await foreach (Album album in cnn.StreamQueryAsync<Album>(sql, ct: cancellationToken))
    count++;
```


## Execute
[Execute SQL](../running-queries/execution.md) and [Supplying values](../running-queries/values.md)

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


## Execute a sequence
[Execute SQL](../running-queries/execution.md), [Build from application logic](../running-queries/builders.md) and [Supplying values](../running-queries/values.md)

```csharp
public record AlbumUpdate(int Id, string Title);

AlbumUpdate[] albums = [new(1, "Blue"), new(2, "Green")];
const string sql = "UPDATE albums SET Title = @Title WHERE AlbumId = @Id";
```

```csharp
// Dapper
cnn.Execute(sql, albums);
```

```csharp
// Rinku
QueryCommand updateAlbum = new(sql);

using DbCommand command = cnn.CreateCommand();
var batch = updateAlbum.StartBuilder(command);

foreach (AlbumUpdate album in albums) {
    batch.UseWith(album);
    batch.Execute();
}
```


## ExecuteScalar<T>
[Execute SQL](../running-queries/execution.md) and [Result shapes](../running-queries/result-shapes.md)

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


## Parameters
[Supplying values](../running-queries/values.md), [Parameter metadata](../running-queries/parameter-metadata.md) and [Parameter binding](../customization/parameters.md)

### Anonymous objects and POCOs
[Supplying values](../running-queries/values.md) and [Parameter members](../customization/parameter-members.md)

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


### Dictionaries
[Supplying values](../running-queries/values.md) and [Parameter binding](../customization/parameters.md)

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


### DynamicParameters / combining parameter sources
[Supplying values](../running-queries/values.md) and [Build from application logic](../running-queries/builders.md)

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


### Explicit parameter metadata
[Parameter metadata](../running-queries/parameter-metadata.md) and [Parameter binding](../customization/parameters.md)

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

static QueryCommand CreateUpdateAlbumPrice() {
    QueryCommand command = new("UPDATE albums SET Price = @price WHERE AlbumId = @albumId");
    command.UpdateParamCache("@price", new ScaledDbParamCache(DbType.Decimal, 18, 2));
    return command;
}

UpdateAlbumPrice.Execute(cnn, new { albumId = 12, price = 12.50m });
```


### DbString
[Parameter metadata](../running-queries/parameter-metadata.md) and [Parameter binding](../customization/parameters.md)

```csharp
// Dapper
const string sql = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";

var title = new DbString { Value = "Blue", IsAnsi = true, Length = 100 };
cnn.Execute(sql, new { albumId = 12, title });
```

```csharp
// Rinku
static readonly QueryCommand UpdateAlbum = CreateUpdateAlbum();

static QueryCommand CreateUpdateAlbum() {
    QueryCommand command = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
    command.UpdateParamCache("@title", TypedDbParamCache.Get(DbType.AnsiString, 100));
    return command;
}

UpdateAlbum.Execute(cnn, new { albumId = 12, title = "Blue" });
```


### null and database NULL
[Supplying values](../running-queries/values.md), [Parameter members](../customization/parameter-members.md) and [Conditional SQL cheat sheet](../conditional-sql/cheatsheet.md)

```csharp
const string sql = "UPDATE albums SET Title = @title WHERE AlbumId = @albumId";
```

```csharp
// Dapper
cnn.Execute(sql, new { albumId = 12, title = (string?)null });
```

```csharp
// Rinku
cnn.Execute(sql, new { albumId = 12, title = DBNull.Value });
```

```csharp
// Dapper - keep the nullable member
public record AlbumTitleUpdate(int AlbumId, string? Title);

var parameters = new AlbumTitleUpdate(12, null);
const string typedSql = "UPDATE albums SET Title = @Title WHERE AlbumId = @AlbumId";

cnn.Execute(typedSql, parameters);
```

```csharp
// Rinku - keep the nullable member
public record AlbumTitleUpdate(int AlbumId, [property: UseDbNull] string? Title);

var parameters = new AlbumTitleUpdate(12, null);
const string typedSql = "UPDATE albums SET Title = @Title WHERE AlbumId = @AlbumId";

cnn.Execute(typedSql, parameters);
```


## IN / collection expansion
[Supplying values](../running-queries/values.md) and [Collection expansion](../conditional-sql/collections.md)

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
```


## Literal replacement
[Conditional SQL cheat sheet](../conditional-sql/cheatsheet.md) and [Supplying values](../running-queries/values.md)

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


## Output parameters
[Stored procedures and output values](../running-queries/stored-procedures.md) and [Parameter metadata](../running-queries/parameter-metadata.md)

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
// Output parameter metadata is discovered by FromProc
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", cnn);

renumberAlbums.Execute(cnn, out DbCommand command, new { albumId = 12 });

using (command) {
    int moved = command.GetOutputValue<int>("@moved");
}
```


### Automatic output write back
[Stored procedures and output values](../running-queries/stored-procedures.md) and [Parameter binding](../customization/parameters.md)

```csharp
public sealed class RenumberArgs {
    public int AlbumId { get; set; }
    public int Moved { get; set; }
}
```

```csharp
// Dapper
RenumberArgs args = new() { AlbumId = 12 };
DynamicParameters parameters = new(args);
parameters.Output(args, (RenumberArgs x) => x.Moved, DbType.Int32);

cnn.Execute("RenumberAlbums", parameters, commandType: CommandType.StoredProcedure);

int moved = args.Moved;
```

```csharp
// Rinku
var args = new RenumberArgs { AlbumId = 12 };
QueryCommand renumberAlbums = QueryCommand.FromProc("RenumberAlbums", cnn);

renumberAlbums.Execute(cnn, out DbCommand command, new { albumId = args.AlbumId });

using (command) {
    args.Moved = command.GetOutputValue<int>("@moved");
}
```


## Dynamic rows
[Dynamic rows](../mapping/dynamic-rows.md) and [Map rows to objects](../mapping/objects.md)

```csharp
const string sql = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = @albumId";
var parameters = new { albumId = 12 };
```

```csharp
// Dapper
dynamic album = cnn.QuerySingle(sql, parameters);
string title = album.Title;

IDictionary<string, object> values = (IDictionary<string, object>)album;
object value = values["Title"];
```

```csharp
// Rinku
DynaObject album = cnn.Query<DynaObject>(sql, parameters);
string title = album.Get<string>("Title");

IReadOnlyDictionary<string, object?> values = album;
object? value = values["Title"];
```


## Constructor mapping
[Construction paths](../mapping/construction-paths.md) and [Reading order](../mapping/reading-order.md)

```csharp
public sealed class Customer {
    public int Id { get; }
    public string Name { get; }

    public Customer(int customerId, string displayName) {
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


## Custom column names / CustomPropertyTypeMap
[Adapt names](../mapping/names.md) and [Type registrations and defaults](../customization/type-registration.md)

```csharp
const string sql = "SELECT customer_id, display_name FROM customers";
```

```csharp
// Dapper
public sealed class Customer {
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

SqlMapper.SetTypeMap(typeof(Customer), new CustomPropertyTypeMap(typeof(Customer), (Type type, string column) => column switch {
    "customer_id" => type.GetProperty(nameof(Customer.Id)),
    "display_name" => type.GetProperty(nameof(Customer.Name)),
    _ => null
}));

IEnumerable<Customer> customers = cnn.Query<Customer>(sql);
```

```csharp
// Rinku
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);

List<Customer> customers = cnn.Query<List<Customer>>(sql);
```

```csharp
// Or configure the type externally
public record ExternalCustomer(int Id, string Name);

TypeParsingInfo.GetOrAdd<ExternalCustomer>().UpdateAltName(names => names.GetDefaultName() switch {
    "Id" => new NameComparer("customer_id"),
    "Name" => new NameComparer("display_name"),
    _ => null
});

List<ExternalCustomer> customers = cnn.Query<List<ExternalCustomer>>(sql);
```

### MatchNamesWithUnderscores

The [name adaptation guide](../mapping/names.md) covers the corresponding Rinku configuration.

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


## Multi mapping / nested objects
[Nested objects](../mapping/nesting.md), [Adapt names](../mapping/names.md) and [Construction paths](../mapping/construction-paths.md)

```csharp
// Dapper
public record User(int Id, string Name);

public sealed class Post {
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public User? Owner { get; set; }
}

const string dapperSql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";

IEnumerable<Post> posts = cnn.Query<Post, User, Post>(dapperSql, (post, owner) => {
    post.Owner = owner;
    return post;
}, splitOn: "Id");
```

### Keep the C# type

Keep the original `Post` and `User` types and alias the nested columns for Rinku.

```csharp
public record User(int Id, string Name);

public sealed class Post {
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public User? Owner { get; set; }
}

TypeParsingInfo.GetOrAdd<User>();

const string rinkuSql = "SELECT p.Id, p.Title, u.Id AS OwnerId, u.Name AS OwnerName FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";

List<Post> posts = cnn.Query<List<Post>>(rinkuSql);
```

### Keep the SQL

Keep Dapper's original SQL and make the nested value unnamed so it starts at the second `Id`.

```csharp
public record User(int Id, string Name) : IDbReadable;
public record Post(int Id, string Title, [NoName] User Owner);

const string dapperSql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";

List<Post> posts = cnn.Query<List<Post>>(dapperSql);
```

### Keep both

Keep the original SQL and types, then configure the `Owner` member externally as an unnamed nested value.

```csharp
public record User(int Id, string Name);

public sealed class Post {
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public User? Owner { get; set; }
}

TypeParsingInfo.GetOrAdd<User>();
TypeParsingInfo.GetOrAdd<Post>().UpdateAltName(names =>
    names.GetDefaultName() == "Owner" ? NoNameComparer.Instance : null);

const string dapperSql = "SELECT p.Id, p.Title, u.Id, u.Name FROM Posts p INNER JOIN Users u ON u.Id = p.UserId";

List<Post> posts = cnn.Query<List<Post>>(dapperSql);
```


## One to many mapping
[Collections from database results](../mapping/collections.md), [Group rows into results](../mapping/grouping.md) and [Nested objects](../mapping/nesting.md)

```csharp
// Dapper
public record Album(int Id, string Title);

public sealed class ArtistWithAlbums {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Album> Albums { get; set; } = [];
}

const string sql = "SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS Id, al.Title FROM artists ar INNER JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId";

List<ArtistWithAlbums> artists = [];
ArtistWithAlbums? current = null;

cnn.Query<ArtistWithAlbums, Album, ArtistWithAlbums>(sql, (artist, album) => {
    if (current is null || current.Id != artist.Id) {
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


## GetRowParser / runtime concrete types
[Construction paths](../mapping/construction-paths.md) and [Complete result parsers](../customization/result-parsers.md)

Dapper selects a row parser inside the read loop. Rinku usually models the same scenario as construction paths on the requested interface, letting one result parser select the concrete value for each row.

```csharp
const string sql = "SELECT Type, Radius, Width, Height FROM Shapes";
```

```csharp
// Dapper
public interface IShape { }

public sealed class Circle : IShape {
    public double Radius { get; set; }
}

public sealed class Rectangle : IShape {
    public double Width { get; set; }
    public double Height { get; set; }
}

using IDataReader reader = cnn.ExecuteReader(sql);

Func<IDataReader, IShape> circleParser = reader.GetRowParser<IShape>(typeof(Circle));
Func<IDataReader, IShape> rectangleParser = reader.GetRowParser<IShape>(typeof(Rectangle));

int typeOrdinal = reader.GetOrdinal("Type");
List<IShape> shapes = [];

while (reader.Read()) {
    IShape shape = reader.GetString(typeOrdinal) switch {
        "Circle" => circleParser(reader),
        "Rectangle" => rectangleParser(reader),
        _ => throw new InvalidOperationException()
    };

    shapes.Add(shape);
}
```

```csharp
// Rinku
public interface IShape : IDbReadable {
    public static IShape Create(string type, double? radius, double? width, double? height) => type switch {
        "Circle" => new Circle { Radius = radius ?? throw new InvalidOperationException() },
        "Rectangle" => new Rectangle {
            Width = width ?? throw new InvalidOperationException(),
            Height = height ?? throw new InvalidOperationException()
        },
        _ => throw new InvalidOperationException()
    };
}

public sealed class Circle : IShape {
    public double Radius { get; set; }
}

public sealed class Rectangle : IShape {
    public double Width { get; set; }
    public double Height { get; set; }
}

List<IShape> shapes = cnn.Query<List<IShape>>(sql);
```


## TypeHandler<T>
[Parameter binding](../customization/parameters.md), [Type registrations and defaults](../customization/type-registration.md) and [Construction paths](../mapping/construction-paths.md)

```csharp
// Dapper
public readonly record struct Money(decimal Value);

public sealed class MoneyHandler : SqlMapper.TypeHandler<Money> {
    public override Money Parse(object value) => new(Convert.ToDecimal(value));

    public override void SetValue(IDbDataParameter parameter, Money value) {
        parameter.DbType = DbType.Decimal;
        parameter.Value = value.Value;
    }
}

SqlMapper.AddTypeHandler(new MoneyHandler());
```

```csharp
// Rinku read
public readonly record struct Money([NoName] decimal Value) : IDbReadable;
public record Invoice(int Id, Money Total);

const string sql = "SELECT InvoiceId AS Id, Total FROM invoices";
List<Invoice> invoices = cnn.Query<List<Invoice>>(sql);
```

```csharp
// Rinku write
sealed class MoneyParamInfo : ConvertedDbParamInfo<Money> {
    protected override object ConvertValue(Money value) => value.Value;
    protected override void ConfigureParameter(IDbDataParameter parameter) => parameter.DbType = DbType.Decimal;
}

static readonly QueryCommand SaveInvoice = CreateSaveInvoice();

static QueryCommand CreateSaveInvoice() {
    QueryCommand command = new("INSERT INTO invoices (Total) VALUES (@total)");
    command.UpdateParamCache("@total", new MoneyParamInfo());
    return command;
}

SaveInvoice.Execute(cnn, new { total = new Money(12.50m) });
```


## ICustomQueryParameter / TVP
[Parameter binding](../customization/parameters.md) and [Parameter metadata](../running-queries/parameter-metadata.md)

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
sealed class SqlServerTableParamInfo(string typeName) : ConvertedDbParamInfo<DataTable> {
    protected override object ConvertValue(DataTable value) => value;

    protected override void ConfigureParameter(IDbDataParameter parameter) {
        SqlParameter sqlParameter = (SqlParameter)parameter;
        sqlParameter.SqlDbType = SqlDbType.Structured;
        sqlParameter.TypeName = typeName;
    }
}

static readonly QueryCommand AlbumsByIds = CreateAlbumsByIds();

static QueryCommand CreateAlbumsByIds() {
    QueryCommand command = new("SELECT a.AlbumId AS Id, a.Title FROM albums a INNER JOIN @ids i ON i.Id = a.AlbumId");
    command.UpdateParamCache("@ids", new SqlServerTableParamInfo("dbo.IntIds"));
    return command;
}

List<Album> albums = AlbumsByIds.Query<List<Album>>(cnn, new { ids });
```


## QueryMultiple / GridReader
[Multiple result sets](../running-queries/multiple-results.md) and [Result shapes](../running-queries/result-shapes.md)

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

### Runtime result Type
[Multiple result sets](../running-queries/multiple-results.md) and [Result shapes](../running-queries/result-shapes.md)

```csharp
Type artistType = typeof(Artist);
```

```csharp
// Dapper
using var results = cnn.QueryMultiple(sql, parameters);
IEnumerable<object> artists = results.Read(artistType);
```

```csharp
// Rinku
Type listType = typeof(List<>).MakeGenericType(artistType);

using MultiReader results = cnn.ExecuteMultiReader(sql, parameters);
IEnumerable<object> artists = (IEnumerable<object>)results.Query(listType);
```


## Stored procedures
[Stored procedures and output values](../running-queries/stored-procedures.md) and [Parameter metadata](../running-queries/parameter-metadata.md)

```csharp
// Dapper
IEnumerable<Album> albums = cnn.Query<Album>("GetAlbumsForArtist", new { artistId = 7 }, commandType: CommandType.StoredProcedure);
```

```csharp
// Rinku - declare the parameter names
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

```csharp
// Or discover the full parameter metadata once
QueryCommand getAlbumsForArtist = QueryCommand.FromProc("GetAlbumsForArtist", cnn);

List<Album> albums = getAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```


## Transactions, timeouts, command type, and cancellation
[Transactions, timeouts, and cancellation](../running-queries/execution-context.md) and [Async execution](../running-queries/async.md)

```csharp
// Dapper
CommandDefinition command = new(sql, parameters, transaction, commandTimeout: 30, commandType: CommandType.Text, cancellationToken: cancellationToken);
IEnumerable<Album> albums = await cnn.QueryAsync<Album>(command);
```


```csharp
// Rinku
List<Album> albums = await cnn.QueryAsync<List<Album>>(sql, parameters, transaction: transaction, timeout: 30, ct: cancellationToken);
```


## Raw DbDataReader
[Raw readers](../running-queries/readers.md) and [Existing DbCommand](../running-queries/dbcommand.md)

```csharp
// Dapper
using IDataReader reader = cnn.ExecuteReader(sql, parameters);
```


```csharp
// Rinku
DbDataReader reader = cnn.ExecuteReader(sql, out DbCommand command, parameters);

using (command)
using (reader) {
    while (reader.Read()) {
        int id = reader.GetInt32(0);
        string title = reader.GetString(1);
    }
}
```


## Query cache / CommandFlags.NoCache
[Cache ownership](../customization/caches.md) and [Complete result parsers](../customization/result-parsers.md)

```csharp
// Dapper
CommandDefinition command = new(sql, parameters, flags: CommandFlags.NoCache);
IEnumerable<Album> albums = cnn.Query<Album>(command);
```

```csharp
// Rinku
QueryCommand getAlbums = ConnectionQueryExtensions.GetOrCreateCommand(sql);

List<Album> albums = getAlbums.Query<List<Album>>(cnn, parameters);
getAlbums.InvalidateParsers(); // closest equivalent, invalidates parsers after execution
```

`CommandFlags.NoCache` prevents Dapper from storing query information for that call. Rinku's closest operation runs through its normal command cache and then invalidates the learned result parsers, so the mechanisms are not identical.


## Dapper.SqlBuilder
[Conditional SQL cheat sheet](../conditional-sql/cheatsheet.md) and [Build from application logic](../running-queries/builders.md)

### Optional WHERE clauses
[Conditional variables](../conditional-sql/variables.md) and [Conditional markers](../conditional-sql/markers.md)

```csharp
int? artistId = 7;
string? title = "Blue";
```

```csharp
// Dapper.SqlBuilder
var builder = new SqlBuilder();
var template = builder.AddTemplate("SELECT AlbumId AS Id, Title FROM albums /**where**/");

if (artistId is not null)
    builder.Where("ArtistId = @artistId", new { artistId });

if (title is not null)
    builder.Where("Title LIKE @title", new { title });

IEnumerable<Album> albums = cnn.Query<Album>(template.RawSql, template.Parameters);
```


```csharp
// Rinku
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7, title = "Blue" });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE @title

albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7, title = (string?)null });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId

albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = (int?)null, title = (string?)null });
// SELECT AlbumId AS Id, Title FROM albums
```

```csharp
// Or build the values incrementally
var builder = SearchAlbums.StartBuilder();
builder.Use("@artistId", 7);
builder.Use("@title", "Blue");

List<Album> albums = builder.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId AND Title LIKE @title

builder.Reset();
albums = builder.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```
