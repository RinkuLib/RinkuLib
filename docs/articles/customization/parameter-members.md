# Parameter source rules

## Built in presence rules

```csharp
public sealed class AlbumSearch
{
    [NotNullOrWhitespace]
    public string? Title { get; init; }

    [NotDefault]
    public int MinimumYear { get; init; }
}
```

[Supplying values](../running-queries/values.md)

## Custom member condition

```csharp
static class SearchRules
{
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class HasTextAttribute : AccessorEmitterHandler
{
    static readonly MethodConditionEmitter Emitter = new(
        typeof(SearchRules).GetMethod(nameof(SearchRules.HasText))
        ?? throw new InvalidOperationException("HasText was not found."));

    public override IAccessorEmitter? GetMemberEmitter(char variableCharacter, int index, Type type, MemberInfo member, Mapper mapper)
        => index < 0 ? null : Emitter;
}
```

```csharp
public sealed class AlbumSearch
{
    [HasText]
    public string? Title { get; init; }
}

static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch { Title = "   " });
// SELECT AlbumId AS Id, Title FROM albums

albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch { Title = "Blue" });
// SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title
```

## Type rule for a key without a member

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
sealed class IncludeDeletedAttribute : AccessorEmitterHandler
{
    static readonly IncludeDeletedEmitter Emitter = new();

    public override ITypeAccessorEmitter? GetTypeEmitter(char variableCharacter, int index, Type type, Mapper mapper)
        => mapper.GetIndex("IncludeDeleted") == index ? Emitter : null;
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

The parameter type has no `IncludeDeleted` member. The type rule supplies that query key.

[`ITypeAccessorEmitter`](xref:Rinku.Querying.Parameters.ITypeAccessorEmitter) handles a key supplied by the type. [`IAccessorEmitter`](xref:Rinku.Querying.Parameters.IAccessorEmitter) handles an existing member slot.
