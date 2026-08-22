# Parameter source rules

Use a custom parameter source rule when a member needs an application specific presence rule.

The built in attributes already cover common cases.

```csharp
public sealed class AlbumSearch
{
    [NotNullOrWhitespace]
    public string? Title { get; init; }

    [NotDefault]
    public int MinimumYear { get; init; }
}
```

See [Supplying values](../running-queries/values.md) for the built in rules.

## Add an application condition

This example supplies a string only when an application method accepts it.

```csharp
static class SearchRules
{
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class HasTextAttribute : AccessorEmitterHandler
{
    static readonly MethodConditionEmitter Emitter = new(typeof(SearchRules).GetMethod(nameof(SearchRules.HasText)) ?? throw new InvalidOperationException("HasText was not found."));

    public override IAccessorEmitter? GetMemberEmitter(char variableCharacter, int index, Type type, MemberInfo member, Mapper mapper) => index < 0 ? null : Emitter;
}
```

Use the attribute on the parameter object.

```csharp
public sealed class AlbumSearch
{
    [HasText]
    public string? Title { get; init; }
}
```

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch
{
    Title = "   "
});
```

The generated SQL does not contain the title condition.

```sql
SELECT AlbumId AS Id, Title FROM albums
```

A value accepted by the rule keeps the condition.

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch
{
    Title = "Blue"
});
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title
```

## Type rules

Apply the handler to the parameter type when the rule needs to provide a key that has no matching member.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
sealed class IncludeDeletedAttribute : AccessorEmitterHandler
{
    static readonly IncludeDeletedEmitter Emitter = new();

    public override ITypeAccessorEmitter? GetTypeEmitter(char variableCharacter, int index, Type type, Mapper mapper) => mapper.GetIndex("IncludeDeleted") == index ? Emitter : null;
}

sealed class IncludeDeletedEmitter : TypeAccessorEmitterBase
{
    protected override void EmitCondition(ILGenerator il, Type type) => il.Emit(OpCodes.Ldc_I4_1);

    protected override void EmitValue(ILGenerator il, Type type)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
    }
}

[IncludeDeleted]
public sealed record AlbumSearch(int ArtistId);
```

The parameter type has no `IncludeDeleted` member. The type rule supplies that key when the query asks for it.

Use `ITypeAccessorEmitter` for a missing key and `IAccessorEmitter` when a type rule needs to replace an existing member rule. A normal member attribute is sufficient when the rule only applies to one member.
