# Construction paths

A type's parsing info holds its construction paths, the constructors and factories the negotiation picks among, plus the members filled afterwards. This page covers how they are ordered and how to add, reorder, or extend them.

## Ordering

Discovery keeps the order paths are declared in and applies one adjustment: a signature that is more specific than one ahead of it moves in front of that one. More specific means equal or more parameters, each the same or a more derived type. At parse time, the first path the columns fully satisfy wins, so the richer paths get their chance first and the lean ones catch what remains, per schema.

```csharp
public class Report {
    public Report(int id) { }                                            // A
    public Report(int id, string title) { }                              // B, more specific than A
    public static Report Load(int id, string title, DateTime at) => new(0); // C, more specific than B
}
// Declared A, B, C. B moves ahead of A, C moves ahead of B.
// Resulting order: C (int, string, DateTime), B (int, string), A (int)

// Columns: Id | Title | At -> C builds with all three
// Columns: Id | Title      -> C fails, B builds
// Columns: Id              -> only A is satisfiable
```

> **Watch for broad paths.** Specificity only reorders comparable signatures. A path that is easy to satisfy and not comparable to the others keeps its spot and can shadow them.
>
> ```csharp
> public class UserProfile {
>     public UserProfile(string username) { }                                       // A
>     public UserProfile(int id) { }                                                // B
>     public UserProfile(int id, string username) { }                               // C, moves ahead of B
>     public static UserProfile Create(int id, string username, DateTime d) => ...; // D, moves ahead of C
> }
> // Resulting order: A, D, C, B
> ```
>
> `A` is not comparable to `D` or `C` (their first parameter is an `int`, not a `string`), so it keeps first place. Any schema with a `Username` column satisfies it, and since `D` and `C` need that column too, they can never win. Ways out:
>
> - Declare the rich constructors first. Discovery keeps that order.
> - Constrain the broad path so it fails when it should not apply: `[CanNotLookAnywhere]` on `username` makes `A` match only when `Username` is the next unconsumed column, leaving `Id`-first schemas to the richer paths.
> - Reorder at runtime, [below](#adding-and-reordering).

## Adding and reordering

`AddPossibleConstruction` accepts any constructor or method whose result is assignable to the target: private constructors, factories on other classes, a derived type's constructor. An added path goes to the front, unless an existing entry is more specific, in which case it settles just behind it.

```csharp
var info = TypeParsingInfo.GetOrAdd<UserProfile>();

// a private constructor
info.AddPossibleConstruction(typeof(UserProfile).GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Guid)])!);

// a factory living elsewhere
info.AddPossibleConstruction(typeof(UserFactory).GetMethod("CreateLegacyUser")!);
```

This composes with polymorphism: registering a derived type's constructor against an interface adds another shape the columns can select.

```csharp
TypeParsingInfo.GetOrAdd<IPayment>()
    .AddPossibleConstruction(typeof(ExternalIdPayment).GetConstructor([typeof(int)])!);
// Columns: OrderId | PaymentExternalId -> Payment is an ExternalIdPayment
```

To carry what `[CanCompleteWithMembers]` or `[AreReadable]` declare, build the path with its flags:

```csharp
var ctor = typeof(LegacyDto).GetConstructor([typeof(int), typeof(string)])!;
if (MethodCtorInfo.TryNew(ctor, MethodCtorInfo.TryMakeParameters(ctor),
        MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers, out var mci))
    info.AddPossibleConstruction(mci);
```

### Generic factories

A construction path can be a generic factory registered against the open definition. The engine closes the method to each closed form at parse time, so one registration serves every `T`.

```csharp
public class Box<T> { internal Box(T value) => Value = value; public T Value { get; } }
public static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);   // external, non-generic host
}

TypeParsingInfo.GetOrAdd(typeof(Box<>))
    .AddPossibleConstruction(typeof(BoxFactory).GetMethod(nameof(BoxFactory.Create))!);

// Box<int> builds through Create<int>, Box<string> through Create<string>, from the one registration
```

Two rules on the method's shape: its type arguments must match the returned type's exactly (order and count), and its declaring type cannot itself be generic, the engine needs a fixed host to resolve the call. A factory declared on the generic type itself is instead non-generic (`static Box<T> Create(T)` inside `Box<T>`), since the type already supplies the parameter, and that form is discovered without registering anything.

### Replacing the set

The set itself is assignable, so the usual move is to take it, apply an ordering rule of your own, and hand it back. Sorting the paths by descending parameter count is one such rule, and the direct fix for the broad-path warning above: the richer paths come first and a broad lean one can no longer shadow them. Any info that implements `ICanProvideConstructions` exposes the set. The default info does; another info may or may not, depending on [which info parses the type](registration.md#registering-with-another-info). So match on the interface, not on a concrete type:

```csharp
if (TypeParsingInfo.GetOrAdd<UserProfile>() is ICanProvideConstructions info) {
    var paths = info.PossibleConstructors.ToArray();
    Array.Sort(paths, (a, b) => b.Parameters.Length - a.Parameters.Length);   // most parameters first
    info.PossibleConstructors = paths;
}
```

Assigning validates every entry (the result must be assignable to the type) and throws otherwise.

## Post-construction members

`AvailableMembers` is the same story for the members filled after construction: public fields (not `readonly` or `const`) and properties with a public setter (`init` excluded). `AddMember` appends one by hand, the counterpart to `AddPossibleConstruction`. It takes a field, a property, or a setter method, an external `static` one taking `(instance, value)` on a non-generic class or an instance one taking `(value)`, and derives the column's type the same way discovery does.

```csharp
// an external setter: static void SetSecretCode(UserProfile profile, string code)
TypeParsingInfo.GetOrAdd<UserProfile>()
    .AddMember(typeof(ExternalLogic).GetMethod("SetSecretCode")!);
// a "SecretCode" column now fills through that setter
```

For finer control, or to add a member the derivation cannot build on its own, hand `AddMember` a `MemberParser` you assembled, or take the whole set through the `ICanProvideMembers` capability and assign it back, exactly as [replacing the set](#replacing-the-set) does for construction paths.

```csharp
if (TypeParsingInfo.GetOrAdd<UserProfile>() is ICanProvideMembers info) {
    var setter = typeof(ExternalLogic).GetMethod("SetSecretCode")!;
    info.AvailableMembers = [.. info.AvailableMembers,
        new MemberParser(setter, ParamInfo.TryNew(setter.GetParameters()[1])!)];
}
```
