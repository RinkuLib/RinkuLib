# Rinku overview

## Query

```csharp
public record Album(int Id, string Title);

static readonly QueryCommand GetAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

List<Album> albums = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
```

The SQL string form accesses a cached `QueryCommand` through the exact SQL string.

```csharp
List<Album> albums = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId", new { artistId = 7 });
```

[Execution](running-queries/execution.md) · [SQL string access](running-queries/sql-string.md)

## Result shapes

```csharp
Album first = GetAlbums.Query<Album>(cnn, new { artistId = 7 });
Optional<Album> maybe = GetAlbums.Query<Optional<Album>>(cnn, new { artistId = 7 });
Single<Album> exactlyOne = GetAlbums.Query<Single<Album>>(cnn, new { artistId = 7 });
List<Album> list = GetAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
Album[] array = GetAlbums.Query<Album[]>(cnn, new { artistId = 7 });
IEnumerable<Album> stream = GetAlbums.Query<IEnumerable<Album>>(cnn, new { artistId = 7 });
```

The requested result type controls how complete mapped values are consumed.

[Result shapes](running-queries/result-shapes.md) · [Streaming](running-queries/streaming.md)

## Map the boundary

```csharp
public record Customer(int Id, string Name);

static readonly QueryCommand GetCustomers = new("SELECT customer_id AS Id, display_name AS Name FROM customers");

List<Customer> customers = GetCustomers.Query<List<Customer>>(cnn);
// SQL aliases adapt the database names.
```

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);

static readonly QueryCommand GetCustomers = new("SELECT customer_id, display_name FROM customers");

List<Customer> customers = GetCustomers.Query<List<Customer>>(cnn);
// Alt adapts the .NET mapping name.
```

```csharp
public record Customer(int Id, string Name);

TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch
    {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });
// The mapping is registered outside SQL and the model.
```

[Names](mapping/names.md) · [Registration](mapping/registration.md)

## Recursive mapping

```csharp
public record Country(int Id, string Name) : IDbReadable;
public record Address(string Street, Country Country) : IDbReadable;
public record Customer(int Id, string Name, Address Address);

static readonly QueryCommand GetCustomer = new("SELECT c.CustomerId AS Id, c.Name, a.Street AS AddressStreet, co.CountryId AS AddressCountryId, co.Name AS AddressCountryName FROM customers c JOIN addresses a ON a.AddressId = c.AddressId JOIN countries co ON co.CountryId = a.CountryId WHERE c.CustomerId = @customerId");

Customer customer = GetCustomer.Query<Customer>(cnn, new { customerId = 12 });
// AddressCountryId fills customer.Address.Country.Id.
// The same mapping process continues through each nested type.
```

```csharp
public record Employee(int Id, string Name, [Alt("Boss")] Employee? Manager = null) : IDbReadable;

static readonly QueryCommand GetEmployee = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerBossId, b.Name AS ManagerBossName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId");

Employee employee = GetEmployee.Query<Employee>(cnn, new { employeeId = 12 });
// ManagerId fills employee.Manager.Id.
// ManagerBossId fills employee.Manager.Manager.Id through the Boss alternate name.
// The default lets this constructor finish when no deeper Manager path exists.
```

[Objects](mapping/objects.md) · [Nested paths](mapping/nesting.md) · [Construction](mapping/construction-paths.md)

## Multi-row folding

```csharp
public record Album(int Id, string Title) : IDbReadable;
public record ArtistWithAlbums(int Id, string Name, [Alt("Album")] List<Album> Albums);

static readonly QueryCommand GetArtists = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS AlbumId, al.Title AS AlbumTitle FROM artists ar JOIN albums al ON al.ArtistId = ar.ArtistId ORDER BY ar.ArtistId");

List<ArtistWithAlbums> artists = GetArtists.Query<List<ArtistWithAlbums>>(cnn);
// List<Album> folds consecutive rows inside one ArtistWithAlbums.
// Album itself is mapped through the same recursive mapping process.
```

[Collections](mapping/collections.md) · [Grouping](mapping/grouping.md) · [Custom multi-row mappings](customization/multi-row.md)

## Conditional SQL

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = ?@artistId AND Title LIKE ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new { artistId = 7 });
// SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId

albums = SearchAlbums.Query<List<Album>>(cnn);
// SELECT AlbumId AS Id, Title FROM albums
```

Collections can expand into database parameters.

```csharp
static readonly QueryCommand GetAlbumsByIds = new("SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_X)");

int[] ids = [2, 5, 9];
List<Album> albums = GetAlbumsByIds.Query<List<Album>>(cnn, new { ids });
// SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId IN (@ids_0, @ids_1, @ids_2)
```

[Conditional variables](conditional-sql/variables.md) · [Collection expansion](conditional-sql/collections.md) · [Markers](conditional-sql/markers.md)

## Builder state

```csharp
var search = SearchAlbums.StartBuilder();

if (artistId is int id)
    search.Use("@artistId", id);

if (!string.IsNullOrWhiteSpace(title))
    search.Use("@title", title);

List<Album> albums = search.Query<List<Album>>(cnn);
```

The builder holds values for this execution flow. The referenced `QueryCommand` does not hold that per-call state.

[Builders](running-queries/builders.md)

## Execution

```csharp
static readonly QueryCommand RenameAlbum = new("UPDATE albums SET Title = @title WHERE AlbumId = @albumId");
static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums WHERE ArtistId = @artistId");
static readonly QueryCommand UpdateArtist = new("UPDATE artists SET ModifiedAt = @modifiedAt WHERE ArtistId = @artistId");

int affected = RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" });
int count = CountAlbums.Query<int>(cnn, new { artistId = 7 });
```

```csharp
List<Album> albums = await GetAlbums.QueryAsync<List<Album>>(cnn, new { artistId = 7 }, ct: cancellationToken);

await foreach (Album album in GetAlbums.StreamQueryAsync<Album>(cnn, new { artistId = 7 }, ct: cancellationToken))
    Console.WriteLine(album.Title);
```

```csharp
using DbTransaction transaction = cnn.BeginTransaction();

RenameAlbum.Execute(cnn, new { albumId = 12, title = "Kind of Blue" }, transaction: transaction);
UpdateArtist.Execute(cnn, new { artistId = 7, modifiedAt = DateTime.UtcNow }, transaction: transaction);

transaction.Commit();
```

[Execution](running-queries/execution.md) · [Async](running-queries/async.md) · [Streaming](running-queries/streaming.md) · [Transactions](running-queries/execution-context.md)

## Several result sets

```csharp
static readonly QueryCommand GetDashboard = new("SELECT ArtistId AS Id, Name FROM artists WHERE ArtistId = @artistId; SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId");

using MultiReader results = GetDashboard.ExecuteMultiReader(cnn, new { artistId = 7 });

Artist artist = results.Query<Artist>();
List<Album> albums = results.Query<List<Album>>();
```

[Multiple result sets](running-queries/multiple-results.md)

## Stored procedures

```csharp
static readonly QueryCommand GetAlbumsForArtist = new("GetAlbumsForArtist", ["artistId"]);

List<Album> albums = GetAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

```csharp
QueryCommand getAlbumsForArtist = QueryCommand.FromProc("GetAlbumsForArtist", cnn);
List<Album> albums = getAlbumsForArtist.Query<List<Album>>(cnn, new { artistId = 7 });
```

[Stored procedures](running-queries/stored-procedures.md)

## Existing ADO.NET commands

```csharp
static readonly CachedTypeParser<Album> AlbumParser = new();

using DbCommand command = cnn.CreateCommand();
command.CommandText = "SELECT AlbumId AS Id, Title FROM albums WHERE AlbumId = 12";

Album album = AlbumParser.Query(command);
```

```csharp
DbDataReader reader = GetAlbums.ExecuteReader(cnn, out DbCommand command, new { artistId = 7 });

using (command)
using (reader)
{
    while (reader.Read())
        Console.WriteLine(reader.GetString(1));
}
```

[Existing DbCommand](running-queries/dbcommand.md) · [Raw readers](running-queries/readers.md)

## Code generation

```csharp
static readonly CachedTypeParser<List<GetAlbumsByArtistResult>> AlbumParser = new();

List<GetAlbumsByArtistResult> albums = AlbumParser.Query(cnn.GetAlbumsByArtist(artistId: 7));
```

Generated methods return `DbCommand` instances.

```csharp
using DbCommand command = cnn.GetAlbumsByArtist(artistId: 7);
using DbDataReader reader = command.ExecuteReader();
```

[Code generation](codegen/index.md) · [Generated code](codegen/generated-code.md) · [Analyzers](codegen/analyzers.md)

## Tracking

```csharp
Album original = new(12, "Blue");
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");

foreach (TrackingChange change in edit.GetChanges())
    Console.WriteLine($"{change.Name} {change.OriginalValue} -> {change.Value}");
```

The same generated item can expose a typed contract.

```csharp
public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    string Title { get; set; }
}

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
edit.Title = "Kind of Blue";
```

[Tracking](tracking/index.md) · [Runtime tracking](tracking/runtime.md) · [Lists](tracking/lists.md)

## Customization

```csharp
public readonly record struct PositionalValue<T>(T Value);

TypeParsingInfo.AddOrSet(typeof(PositionalValue<>), CtorTypeInfo.Instance);

static readonly QueryCommand CountAlbums = new("SELECT COUNT(*) FROM albums");
PositionalValue<int> count = CountAlbums.Query<PositionalValue<int>>(cnn);
```

[Type registration](customization/type-registration.md) · [Complete result parsers](customization/result-parsers.md)

[Custom multi-row mappings](customization/multi-row.md) · [Parameter binding](customization/parameters.md) · [Method caller](customization/method-caller.md) · [Cache control](customization/caches.md) · [Advanced customization](customization/index.md)
