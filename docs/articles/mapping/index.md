# Mapping

Ask for a type, get instances back.

```csharp
public record Album(int Id, string Title);

List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

Any class, record, or struct works. The engine matches the result columns to a constructor, a factory, or settable members, by name and type. No attributes, no base class, no configuration for the common case.

Flat rows map onto nested shapes. Columns prefixed with a member's name fill that member.

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);

// Columns: Id | Title | ArtistId | ArtistName
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
// albums[0].Artist.Name is filled from ArtistName
```

There is one rule to know up front. A type you query directly is known automatically. A type reached only through another one must be registered. The `IDbReadable` marker on `Artist` above is the simplest way. Details on [registration](registration.md).

The engine is one negotiation composed of small parts, each a default implementation that can be swapped. The behaviors in this section, from nesting to tuples, are arrangements of those parts.

## In this section

- [Objects and nesting](objects.md). Constructors, factories, members, prefixes, recursion.
- [Nullability](nullability.md). What a `NULL` column does, and how to change it.
- [DynaObject](dynaobject.md). Rows without a fixed type.
- [Registration](registration.md). How a type becomes known, and which rules it signs up for.
- [Construction paths](construction-paths.md). How paths are ordered, added, and reordered.
- [Names](names.md). The matching rules and their adjustments.
- [Reading order](reading-order.md). Whether a slot holds the line or searches the schema.
- [Parsers](parsers.md). How the parser for a `T` is picked, added, or driven directly.

The result wrappers (`List<T>`, `Optional<T>`, streaming) are on [result shapes](../running-queries/result-shapes.md).
