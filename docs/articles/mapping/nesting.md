# Nested objects

Prefix nested columns with the member name.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

static readonly QueryCommand GetAlbum = new("SELECT al.AlbumId AS Id, al.Title, ar.ArtistId AS ArtistId, ar.Name AS ArtistName FROM albums al JOIN artists ar ON ar.ArtistId = al.ArtistId WHERE al.AlbumId = @albumId");

Album album = GetAlbum.Query<Album>(cnn, new { albumId = 1 });
```

`Id` fills `Album.Id`. `ArtistId` fills `Album.Artist.Id`. `ArtistName` fills `Album.Artist.Name`.

## Register nested types

A type reached through another mapped object must be readable.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
```

Or register it during application setup.

```csharp
TypeParsingInfo.GetOrAdd<Artist>();
```

Without a readable registration, the nested construction path is unavailable.

See [registration](registration.md) for changing mapping rules without modifying the mapped type.

## More than one level

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

## Accept another prefix

Use `[Alt]` when the SQL uses another name for a nested member.

```csharp
public record Address(string City, string PostalCode) : IDbReadable;
public record Customer(int Id, string Name, [Alt("ShipTo")] Address ShippingAddress);
```

Both `ShippingAddressCity` and `ShipToCity` can match the nested path.

See [name rules](names.md) for deeper prefix changes.

## Missing nested rows

A left join can return database `NULL` for every child column. Put `[AbortOnNull]` on the child identity and make the containing member nullable.

```csharp
public record Album([AbortOnNull] int Id, string Title) : IDbReadable;
public record Artist(int Id, string Name, Album? LatestAlbum);

static readonly QueryCommand GetArtist = new("SELECT ar.ArtistId AS Id, ar.Name, al.AlbumId AS LatestAlbumId, al.Title AS LatestAlbumTitle FROM artists ar LEFT JOIN albums al ON al.AlbumId = ar.LatestAlbumId WHERE ar.ArtistId = @artistId");

Artist artist = GetArtist.Query<Artist>(cnn, new { artistId = 1 });
```

When `LatestAlbumId` is database `NULL`, construction of `Album` stops and `LatestAlbum` becomes null.

See [database NULL](nulls.md) for null propagation through deeper objects.
