# Custom member rules

Use an attribute when a parameter member needs a rule that Rinku does not provide.

```csharp
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
```

## A method decides when a member is used

Write a static method that takes the member value and returns `true` when the member is used.

```csharp
static class SearchRules {
    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class HasTextAttribute : AccessorEmitterHandler {
    private static readonly MethodConditionEmitter Emitter = new(
        typeof(SearchRules).GetMethod(nameof(SearchRules.HasText))!);

    public override IAccessorEmitter? GetMemberEmitter(
        char varChar, int index, Type type, MemberInfo member, Mapper mapper)
        => index < 0 ? null : Emitter;
}
```

Put the attribute on the parameter member.

```csharp
public sealed class TrackSearch {
    [HasText] public string? Composer { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new TrackSearch { Composer = "  " });
// @composer is not supplied

SearchCmd.Query<List<Track>>(cnn, new TrackSearch { Composer = "AC/DC" });
// @composer is supplied
```

Pass `invert: true` when the method instead returns `true` for a value that must stay out.

```csharp
private static readonly MethodConditionEmitter Emitter = new(
    typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!,
    invert: true);
// the member is used only when it has text
```

## A custom condition and value

Derive from `AccessorEmitterBase` when the rule has the usual shape: test a member, then provide a value.

```csharp
sealed class PositiveNumberEmitter : AccessorEmitterBase {
    protected override void EmitCondition(ILGenerator il, Type type, MemberInfo member) {
        AccessorEmitter.EmitMemberLoad(il, type, member);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
    }

    protected override void EmitValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberValue(il, type, member);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
sealed class PositiveNumberAttribute : AccessorEmitterHandler {
    private static readonly PositiveNumberEmitter Emitter = new();

    public override IAccessorEmitter? GetMemberEmitter(
        char varChar, int index, Type type, MemberInfo member, Mapper mapper)
        => index < 0 ? null : Emitter;
}
```

Return it from an attribute the same way as `HasTextAttribute`.

```csharp
public sealed class PriceSearch {
    [PositiveNumber] public int MinPrice { get; init; }
}

SearchCmd.Query<List<Track>>(cnn, new PriceSearch { MinPrice = 0 });
// @minPrice is not supplied

SearchCmd.Query<List<Track>>(cnn, new PriceSearch { MinPrice = 10 });
// @minPrice is supplied
```

## A rule with its own flow

Implement `IAccessorEmitter` when the rule does not test usability first. `UseDbNull` is one built-in example.

```csharp
public sealed class UpdateTrack {
    [UseDbNull] public string? Composer { get; init; }
}

update.Execute(cnn, new UpdateTrack { Composer = null });
// @Composer is supplied as SQL NULL
```

An `IAccessorEmitter` writes both paths:

```csharp
sealed class MyRuleEmitter : IAccessorEmitter {
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        // emit direct command binding
    }

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue) {
        // emit the builder value slot
    }

    public void Validate(Type type, MemberInfo member) {
        // reject unsupported member types here
    }
}
```

Use `ITypeAccessorEmitter` in the same way for an attribute placed on the whole parameter type.

An attribute on the type can also return an `IAccessorEmitter` from `GetMemberEmitter`. It becomes the
default rule for every matching member. A member attribute takes priority.

```csharp
[UseDbNull]
public sealed class UpdateTrack {
    public string? Composer { get; init; }
    [NotNullOrWhitespace] public string? Name { get; init; }
}

update.Execute(cnn, new UpdateTrack { Composer = null, Name = null });
// @Composer is SQL NULL
// @Name is not supplied
```
