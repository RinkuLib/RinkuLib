# Parameter member rules

An `AccessorEmitterHandler` attribute changes whether one parameter member is supplied and which value it contributes.

See [supplying values](../running-queries/values.md) for `[UseDbNull]`, `[NotNullOrWhitespace]`, `[NotDefault]`, and the boolean-condition attributes.

## Use a condition method

`MethodConditionEmitter` calls a static Boolean method before supplying the member.

```csharp
static class SearchRules {
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class HasTextAttribute : AccessorEmitterHandler {
    static readonly MethodConditionEmitter Emitter = new(
        typeof(SearchRules).GetMethod(nameof(SearchRules.HasText)) ?? throw new InvalidOperationException("HasText was not found."));

    public override IAccessorEmitter? GetMemberEmitter(char variableCharacter, int index, Type type, MemberInfo member, Mapper mapper) => index < 0 ? null : Emitter;
}

public sealed class AlbumSearch {
    [HasText] public string? Title { get; init; }
}
```

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE Title = ?@title");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch { Title = "   " });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

```csharp
List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch { Title = "Blue" });
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE Title = @title
```

Pass `true` as the second `MethodConditionEmitter` constructor argument when the condition method returns true for values that should be excluded.

## Emit a low-level condition

`AccessorEmitterBase` supplies the common branching flow. The subclass emits the condition and value.

```csharp
sealed class PositiveNumberEmitter : AccessorEmitterBase {
    protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member, int sourceArgument) {
        AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
    }

    protected override void EmitValue(ILGenerator il, Type type, MemberInfo member, int sourceArgument) => AccessorEmitter.EmitMemberValue(il, type, member, sourceArgument);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class PositiveNumberAttribute : AccessorEmitterHandler {
    static readonly PositiveNumberEmitter Emitter = new();

    public override IAccessorEmitter? GetMemberEmitter(char variableCharacter, int index, Type type, MemberInfo member, Mapper mapper) => index < 0 ? null : Emitter;
}

public sealed class AlbumSearch {
    [PositiveNumber] public int MinimumYear { get; init; }
}
```

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ReleaseYear >= ?@minimumYear");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch { MinimumYear = 0 });
```

```sql
SELECT AlbumId AS Id, Title FROM albums
```

## Control direct values and `UseWith`

Implement `IAccessorEmitter` when direct parameter objects and builder `UseWith` need a custom value flow.

```csharp
sealed class NullAsDbNullEmitter : IAccessorEmitter {
    static readonly MethodInfo ToDbValueMethod =
        typeof(NullAsDbNullEmitter).GetMethod(nameof(ToDbValue), BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("ToDbValue was not found.");

    static object ToDbValue(string? value) => (object?)value ?? DBNull.Value;

    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => AccessorEmitter.EmitSlot(
            il,
            index,
            key,
            handlerValues,
            handlerIndex,
            handlerValue,
            bindValue,
            condition => condition.Emit(OpCodes.Ldc_I4_1),
            value => EmitValue(value, type, member, 0));

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
        => AccessorEmitter.EmitUseWithSlot(il, index, bindValue, condition => condition.Emit(OpCodes.Ldc_I4_1), value => EmitValue(value, type, member, context.SourceArgument), context);

    public void Validate(Type type, MemberInfo member) {
        Type memberType = member is FieldInfo field
            ? field.FieldType
            : ((PropertyInfo)member).PropertyType;

        if (memberType != typeof(string))
            throw new InvalidOperationException("NullAsDbNull requires a string member.");
    }

    static void EmitValue(ILGenerator il, Type type, MemberInfo member, int sourceArgument) {
        AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
        il.Emit(OpCodes.Call, ToDbValueMethod);
    }
}
```

Return the emitter from an `AccessorEmitterHandler` attribute like the earlier examples.

## Supply a key with no matching member

Apply an `AccessorEmitterHandler` to the parameter-object type and return an `ITypeAccessorEmitter` for a query key that has no field or property.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
sealed class IncludeDeletedAttribute : AccessorEmitterHandler {
    static readonly IncludeDeletedEmitter Emitter = new();

    public override ITypeAccessorEmitter? GetTypeEmitter(char variableCharacter, int index, Type type, Mapper mapper) => mapper.GetIndex("IncludeDeleted") == index
            ? Emitter
            : null;
}

sealed class IncludeDeletedEmitter : TypeAccessorEmitterBase {
    protected override void EmitCondition(ILGenerator il, Type type) => il.Emit(OpCodes.Ldc_I4_1);

    protected override void EmitValue(ILGenerator il, Type type) {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
    }
}

[IncludeDeleted]
public sealed record AlbumSearch(int ArtistId);
```

`AlbumSearch` has no `IncludeDeleted` member. The type attribute supplies that condition key during accessor generation.

```csharp
static readonly QueryCommand SearchAlbums = new("SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId /*IncludeDeleted*/OR IsDeleted = 1");

List<Album> albums = SearchAlbums.Query<List<Album>>(cnn, new AlbumSearch(12));
```

```sql
SELECT AlbumId AS Id, Title FROM albums WHERE ArtistId = @artistId OR IsDeleted = 1
```

`TypeAccessorEmitterBase` handles direct parameter objects and builder `UseWith`. The custom emitter supplies only its presence condition and value.

`GetTypeEmitter(...)` is considered only when no member matches the key. To impose a type-wide rule on existing members, override `GetMemberEmitter(...)` on the type-level attribute and return an `IAccessorEmitter`.

## Override existing members from the type

A built-in type-level attribute applies its rule to matching members. A member attribute can still replace it.


```csharp
[UseDbNull]
public sealed class AlbumUpdate {
    public int AlbumId { get; init; }
    public string? Title { get; init; }

    [NotNullOrWhitespace]
    public string? Notes { get; init; }
}
```

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = ?@title, Notes = ?@notes WHERE AlbumId = @albumId");

UpdateAlbum.Execute(cnn, new AlbumUpdate { AlbumId = 12, Title = null, Notes = null });
```

```sql
UPDATE albums SET Title = @title WHERE AlbumId = @albumId
-- @title contains database NULL. @notes is absent.
```
