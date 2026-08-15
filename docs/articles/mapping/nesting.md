# Nested objects

A mapped object can contain another mapped object. Prefix the nested columns with the member name.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbum = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

```text
Id  Title  ArtistId  ArtistName
1   Blue   7         Joni Mitchell
```

`Album.Id` uses `Id`. `Album.Artist.Id` uses `ArtistId`, and `Album.Artist.Name` uses `ArtistName`.

## Register nested types

The root type is explicitly requested by `Query<Album>`. A type reached through another mapped object must be readable too.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
```

Without `IDbReadable` or another registration, the `Artist` construction path is unavailable and the query raises `RINKU3001`.

```csharp
public record Artist(int Id, string Name);
public record Album(int Id, string Title, Artist Artist);

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
// RINKU3001
```

Types can also be registered during application setup.

```csharp
TypeParsingInfo.GetOrAdd<Artist>();
```

## Nest more than one level

Every member name adds another prefix segment.

```csharp
public record Country(int Id, string Name) : IDbReadable;
public record Address(string City, Country Country) : IDbReadable;
public record Customer(int Id, string Name, Address BillingAddress);
```

```text
Id
Name
BillingAddressCity
BillingAddressCountryId
BillingAddressCountryName
```

```csharp
Customer customer = GetCustomer.Query<Customer>(cnn, new { customerId = 1 });
```

## Accept another prefix

Use `[Alt]` when the SQL uses a different name for the nested value.

```csharp
public record Address(string City, string PostalCode) : IDbReadable;
public record Customer(int Id, string Name, [Alt("ShipTo")] Address ShippingAddress);
```

Both prefix sets can fill `ShippingAddress`.

```text
ShippingAddressCity | ShippingAddressPostalCode
ShipToCity          | ShipToPostalCode
```

Aliases in the SQL are often simpler when the query is under application control.

```sql
SELECT c.CustomerId AS Id, c.Name, a.City AS ShippingAddressCity, a.PostalCode AS ShippingAddressPostalCode
FROM customers c
JOIN addresses a ON a.AddressId = c.ShippingAddressId
WHERE c.CustomerId = @customerId
```

## Handle a missing nested row

A `LEFT JOIN` returns database `NULL` when the related row is missing. Put `[AbortOnNull]` on the nested identity and make the containing member nullable.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);

static readonly QueryCommand GetArtist = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId WHERE ar.ArtistId = @artistId");

Artist artist = GetArtist.Query<Artist>(cnn, new { artistId = 1 });
```

When `LatestAlbumId` is `NULL`, construction of `Album` stops and `artist.LatestAlbum` becomes null.

Without `[AbortOnNull]`, the non-nullable `Album.Id` rejects database `NULL`.

## Complete a constructor with a nested member

A parameterized constructor normally consumes only its own parameters. `[CanCompleteWithMembers]` allows remaining columns to fill writable members, including nested values.

```csharp
public sealed class Album {
    [CanCompleteWithMembers]
    public Album(int id, string title) => (Id, Title) = (id, title);

    public int Id { get; }
    public string Title { get; }
    public Artist? Artist { get; set; }
}
```

The result must still use `ArtistId` and `ArtistName`, and `Artist` must be registered as a readable nested type.

[Registration](registration.md) covers every way to make nested types readable. [Names](names.md) covers prefix changes. [Database NULL](nulls.md) covers missing objects. [Collections](collections.md) continues with one-to-many relationships.
