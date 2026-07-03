# Registration

A type must be known to the engine before it will map to it. This page covers how that happens, how construction paths are ordered, and how to add paths reflection would not find.

## How a type becomes known

1. **Queried directly.** The `T` in `Query<T>` (including the element of `List<T>`, `Optional<T>`) registers itself on first use.
2. **Basic types and enums.** Anything a `DbDataReader` exposes directly, plus every enum, works on contact.
3. **The `IDbReadable` marker.** A type implementing `IDbReadable` is registered wherever it appears, including as a nested slot. This is the usual answer for nested custom types.
4. **Manually.**

```csharp
var info = TypeParsingInfo.GetOrAdd<Address>();                 // register and configure
var open = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));   // generic definition
```

A custom type used only inside another type, with none of the above, is not considered. Its construction path is treated as unsatisfiable.

There is also `[AreReadable]` on a constructor or factory: it registers all of that method's parameter types, so one entry point makes a whole graph mappable.

## Path ordering

Discovery keeps the order paths are found in and applies one adjustment: a signature that is more specific than one ahead of it moves in front of that one. More specific means equal or more parameters, each the same or a more derived type.

```csharp
public class UserProfile {
    public UserProfile(string username) { }                                  // A
    public UserProfile(int id) { }                                           // B
    private UserProfile(Guid internalId) { }                                 // ignored, private
    public UserProfile(int id, string username) { }                          // C, more specific than B
    public static UserProfile Create(int id, string username, DateTime d) => ...; // D, more specific than C
    public UserProfile(DateTime expiry, bool isAdmin) { }                    // E, unrelated
}
// Resulting order: A, D, C, B, E
```

The first path the columns fully satisfy wins. To pin one constructor and skip ordering entirely, mark it `[DbConstructor]`.

## Adding paths by hand

Any `MethodBase` whose result is assignable to the target works: private constructors, factories on other classes, a derived type's constructor.

```csharp
var info = TypeParsingInfo.GetOrAdd<UserProfile>();

// a private constructor
info.AddPossibleConstruction(typeof(UserProfile).GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Guid)])!);

// a factory living elsewhere
info.AddPossibleConstruction(typeof(UserFactory).GetMethod("CreateLegacyUser")!);
```

This composes with polymorphism. Registering a derived type's constructor against an interface adds another shape the columns can select:

```csharp
TypeParsingInfo.GetOrAdd<IPayment>()
    .AddPossibleConstruction(typeof(ExternalIdPayment).GetConstructor([typeof(int)])!);
// Columns: OrderId | PaymentExternalId  -> Payment is an ExternalIdPayment
```

A manually added path goes to the front, unless an existing entry is more specific, in which case it settles just behind it. To replace the whole set, assign `PossibleConstructors`. Discovery is lazy, so call `info.Init()` first when you want the discovered set present before editing.

## Generic types

A generic type registers as its definition (`Result<>`, not `Result<int>`), so one configuration covers every closed form. An exact closed-type entry wins over the definition when both exist.

Constraints on generic entry points:

- A generic method's type arguments must match the target's exactly, in order and count.
- The declaring type of a factory cannot itself be generic.

```csharp
public class DataWrapper<T> { public DataWrapper(T value) { } }

public static class WrapperFactory {                    // valid, non-generic host
    public static DataWrapper<T> Create<T>(T value) => new(value);
}
public static class GenericFactory<T> {                 // invalid, generic host
    public static DataWrapper<T> Create(T value) => new(value);
}
```

## Post-construction members

`AvailableMembers` is the set filled after construction: public fields (not `readonly` or `const`) and properties with a public setter (`init` excluded). You can add entries by hand, including private members or an external static setter, a `static` method taking `(instance, value)` on a non-generic class.

```csharp
var method  = typeof(ExternalLogic).GetMethod("SetSecretCode")!;   // static (UserProfile, string)
var matcher = ParamInfo.TryNew(method.GetParameters()[1]);
var member  = new MemberParser(method, matcher);
```

Because members run after the constructor, a member that shares a column with a constructor parameter overwrites it. Remove it from `AvailableMembers`, or make it non-public, to prevent that.
