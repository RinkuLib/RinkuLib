# Complex objects

*Constructors, factories, members, and nesting.*

Most rows map to more than a flat bag of primitives. The engine builds a complex type by finding a **construction path**, a constructor or static factory whose parameters the schema can fill, then optionally filling settable members afterward.

## What the engine can use

Out of the box, with no annotations:

- **Public constructors**, matched by parameter name and type against the columns.
- **Public static factory methods** that return the exact type (non-generic).
- **Settable members after construction**. Public fields and properties with a public setter (see [below](#post-construction-members)).

```csharp
public record Track(int Id, string Name, decimal UnitPrice);  // ctor params match columns

public class Playlist {
    public Playlist(int id) { Id = id; }                      // ctor fills Id
    public int Id { get; }
    public string? Name { get; set; }                         // settable, filled after
}
```

A column matches a parameter or member when the **name matches** (case-insensitive) and the **type is compatible**. Among the viable constructors, the engine takes the **first one the schema can fully satisfy**. The paths are kept in priority order (more specific first), so that first match is usually the best. The exact ordering rule is in [registration & discovery](registration-and-discovery.md).

## Nesting

When a parameter or member is itself a complex type, the engine recurses. Nested members are matched with a **name prefix** built from the path, so columns like `ArtistId` and `ArtistName` line up with a nested `Artist.Id` and `Artist.Name`.

```csharp
public record Album(int Id, string Title, Artist Artist);
public record Artist(int Id, string Name) : IDbReadable;   // see note below

// Schema: Id | Title | ArtistId | ArtistName
List<Album> albums = GetAlbums.Query<List<Album>>(cnn);
```

The prefix grows as nesting deepens, and it keeps two nested objects of the same shape from fighting over columns. To add extra accepted names to a slot, use `[Alt("...")]`. The full set of name controls is in [custom parsing & matching](custom-parsing.md).

> **A nested type must be registered.** `Album` is registered automatically because you query it directly, but `Artist` is only reached through `Album`, so the engine won't consider it until it's known. The easiest way is the `IDbReadable` marker interface (shown above). You can also register it explicitly or query it directly somewhere first. See [registration & discovery](registration-and-discovery.md).

## Post-construction members

After the constructor runs, the engine can keep filling **public settable fields and properties** from columns that weren't consumed. Two things to know:

- By default, post-construction filling only happens when the chosen path is the **parameterless constructor**. To allow it on any constructor or factory, mark that path with `[CanCompleteWithMembers]`.
- `init`-only properties can't take part. They run only during construction.

If both a constructor parameter and a settable member map to the same column, the member assignment runs **after** the constructor and wins. See [registration & discovery](registration-and-discovery.md#post-construction-members) for how to avoid that.

## Recursion and self-reference

The same negotiation runs at every depth, so recursive shapes map fine as long as each level's columns are distinguishable by the name prefix. An `Employee` with a nested `Manager` of the same type is a typical case, where its `ManagerId` and `ManagerName` columns sit under the `Manager` prefix.