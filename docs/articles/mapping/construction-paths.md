# Construction paths

Rinku can create an object through constructors, static factories, and registered construction paths.

```csharp
public sealed class Album
{
    public Album(int id, string title) { }
    public Album(int id, string title, string notes) { }
}
```

The returned columns decide which path is usable.

## Selection order

A construction path must be able to satisfy every required input.

```csharp
public record Album(int Id, string Title, string? Notes = null);

Album shortRow = GetAlbum.Query<Album>(cnn);
Album longRow = GetAlbumWithNotes.Query<Album>(cnn);
```

The shorter row can use the declared default. The longer row can use all three columns.

When several paths are usable, the configured path order decides which one wins.

## Select one constructor

Use the type registration during setup when another constructor should be registered explicitly.

```csharp
ConstructorInfo constructor = typeof(Album).GetConstructor([typeof(int), typeof(string)])!;
TypeParsingInfo.GetOrAdd<Album>().AddPossibleConstruction(constructor);
```

Use the exact constructor or factory reflected from the real type.

## Add a constructor or factory

A non public constructor or external factory can be added during setup.

```csharp
ConstructorInfo constructor = typeof(Album).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [typeof(int), typeof(string)], modifiers: null)!;

TypeParsingInfo.GetOrAdd<Album>().AddPossibleConstruction(constructor);
```

The same registration model can add a static factory method.

## Complete with writable members

A construction path can allow remaining columns to fill writable members.

```csharp
public sealed class Album
{
    [CanCompleteWithMembers]
    public Album(int id) => Id = id;

    public int Id { get; }
    public string? Title { get; set; }
}
```

Use this when part of the result belongs to the constructor and the rest belongs to members.

## Change path order or fallback behavior

Use the registration APIs when application conventions need another order or fallback.

Keep those changes in application setup before parsers are created.

The [advanced type registration](../customization/type-registration.md) page shows how to replace type level mapping behavior. The [API reference](../../api/index.md) contains the individual construction configuration members.
