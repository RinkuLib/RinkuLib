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

To pin one path and skip ordering entirely, mark it `[DbConstructor]`.

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

The set itself is assignable, and the usual move is not replacing it but taking it, rearranging, and handing it back. This is the direct fix for the broad-path warning above. The paths live on the default info, what a normal registration gets, so match on it rather than cast, the type may have been [registered with another info](registration.md#registering-with-another-info):

```csharp
if (TypeParsingInfo.GetOrAdd<UserProfile>() is DefaultTypeParsingInfo info) {
    info.Init();                                // discovery is lazy, fill the set first

    var paths = info.PossibleConstructors.ToArray();
    // order was A, D, C, B: move the broad (string username) path last
    (paths[0], paths[1], paths[2], paths[3]) = (paths[1], paths[2], paths[3], paths[0]);
    info.PossibleConstructors = paths;
}
```

Assigning validates every entry (the result must be assignable to the type) and throws otherwise.

## Post-construction members

`AvailableMembers` is the same story for the members filled after construction: public fields (not `readonly` or `const`) and properties with a public setter (`init` excluded). Entries can be added by hand, including an external static setter, a `static` method taking `(instance, value)` on a non-generic class.

```csharp
if (TypeParsingInfo.GetOrAdd<UserProfile>() is DefaultTypeParsingInfo info) {
    info.Init();

    // an external setter: static void SetSecretCode(UserProfile profile, string code)
    var setter = typeof(ExternalLogic).GetMethod("SetSecretCode")!;
    MemberParser[] members = [.. info.AvailableMembers,
        new MemberParser(setter, ParamInfo.TryNew(setter.GetParameters()[1])!)];
    info.AvailableMembers = members;
    // a "SecretCode" column now fills through that setter
}
```
