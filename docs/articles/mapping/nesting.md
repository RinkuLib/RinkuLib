# Recursive mapping

## Member paths

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbum = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 12 });
// Id fills album.Id.
// ArtistId fills album.Artist.Id.
// ArtistName fills album.Artist.Name.
```

The same mapping process continues through deeper type shapes.

```csharp
public record Country(int Id, string Name) : IDbReadable;
public record Address(string City, Country Country) : IDbReadable;
public record Customer(int Id, string Name, Address BillingAddress);

Customer customer = cnn.Query<Customer>("SELECT c.CustomerId AS Id, c.Name, a.City AS BillingAddressCity, co.CountryId AS BillingAddressCountryId, co.Name AS BillingAddressCountryName FROM customers c JOIN addresses a ON a.AddressId = c.BillingAddressId JOIN countries co ON co.CountryId = a.CountryId WHERE c.CustomerId = @customerId", new { customerId = 12 });
```

## Name adaptation inside the path

```csharp
public record Address([Alt("Postal")] int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);

Person person = cnn.Query<Person>("SELECT PersonId AS Id, PostalCode AS HomePostal, City AS HomeCity FROM people WHERE PersonId = @personId", new { personId = 12 });
// HomePostal reaches Home.Zip.
```

[Name adaptation](names.md)

## Same type again

```csharp
public record Employee(int Id, string Name, [Alt("Boss")] Employee? Manager = null) : IDbReadable;

static readonly QueryCommand GetEmployee = new("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerBossId, b.Name AS ManagerBossName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId");

Employee employee = GetEmployee.Query<Employee>(cnn, new { employeeId = 12 });
// Manager is another Employee.
// Manager.Manager is another Employee.
// The deepest Employee can finish through the construction path where Manager uses its default.
```

The recursive type does not need a special recursion mapping. Construction only needs an alternative that can finish when no deeper matching shape is available.

[Construction paths](construction-paths.md)

## Readable nested types

```csharp
public record Artist(int Id, string Name) : IDbReadable;
```

The same mapping registration can live outside the type.

```csharp
public record Artist(int Id, string Name);

TypeParsingInfo.GetOrAdd<Artist>();
```

[Registration](registration.md)

## Missing nested value

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);

static readonly QueryCommand GetArtist = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId WHERE ar.ArtistId = @artistId");

Artist artist = GetArtist.Query<Artist>(cnn, new { artistId = 7 });
// NULL LatestAlbumId aborts Album construction.
// LatestAlbum receives the missing nested value.
```

[Database NULL](nulls.md)
