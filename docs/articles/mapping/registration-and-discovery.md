# Registration & discovery

*How the engine finds construction paths, and how to steer it.*

A type has to be **registered** before the engine will map to it, and there are clear rules for how that happens. This page also covers how, once a type is known, its construction paths are discovered and ordered.

## How a type becomes known

The distinction that matters. A type asked for **directly** is registered for you. A type reached only **through another** is not.

1. **Queried directly.** The `T` in `Query<T>` (and the element type of `List<T>`, `Optional<T>`) is registered the first time you request it.
2. **Basic types and enums.** Anything a `DbDataReader` exposes directly, plus every enum, is usable on contact.
3. **Marker interface.** A type implementing `IDbReadable` is registered automatically wherever it is seen, including as a nested parameter or member. This is the simplest way to make your own types usable indirectly.
4. **Manual.** Register explicitly to configure a type up front, or to add construction paths reflection wouldn't find.

```csharp
var info = TypeParsingInfo.GetOrAdd<Address>();                 // make Address usable and configurable
var open = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));   // generic definition
```

A type used **only** as a constructor parameter or member of another type, and that is not a basic type, an enum, `IDbReadable`, or already registered, won't be considered. That construction path is treated as unsatisfiable. So a nested custom type needs path 3 or 4 (or to have been queried directly earlier).

## Discovery criteria

When it inspects a type, the engine considers.

- **Public constructors.**
- **Public static factory methods** that are **non-generic** and return the **exact** target type.
- A path is viable only if **every parameter** is something the engine can handle. A basic type or enum, an `IDbReadable` type, a generic placeholder (resolved later), or a type already registered.

## The specificity rule

The engine keeps discovery order and only does a **move-forward**. A signature that is more specific than one ahead of it jumps directly in front of that one. A signature is more specific when it has **equal or more parameters** and **every parameter** is the same type as, or a more derived type than, the matching one.

```csharp
public class UserProfile {
    public UserProfile(string username) { }                          // A
    public UserProfile(int id) { }                                   // B
    private UserProfile(Guid internalId) { }                         // ignored, private
    public UserProfile(int id, string username) { }                  // C, more specific than B
    public static UserProfile Create(int id, string username, DateTime last) => ...; // D, more specific than C
    public UserProfile(DateTime expiry, bool isAdmin) { }            // E, unrelated
    public static object Build(int id) => ...;                       // ignored, wrong return type
}
```

Resulting order: `(string)` A, `(int,string,DateTime)` D, `(int,string)` C, `(int)` B, `(DateTime,bool)` E. `A` stays on top because D and C are only more specific than **B**, not than A.

To pin one constructor and skip this entirely, mark it `[DbConstructor]` (used when a type is registered through `CtorTypeInfo`).

## Manual registration

The engine accepts any `MethodBase` whose result is stack-equivalent to the target type, so you can register private constructors, factories on other classes, or a derived type's constructor.

```csharp
var info = TypeParsingInfo.GetOrAdd<UserProfile>();

// private constructor
info.AddPossibleConstruction(typeof(UserProfile).GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Guid) }, null));

// external factory
info.AddPossibleConstruction(typeof(UserFactory).GetMethod("CreateLegacyUser"));
```

A manually added construction is **high priority**. It moves to the top unless an existing entry is more specific, in which case it settles just behind it. To replace the whole set, assign `PossibleConstructors` (`ReadOnlySpan<MethodCtorInfo>`). Because discovery is lazy, call `info.Init()` first if you want the full discovered set. Setting it validates every entry is stack-equivalent and throws otherwise. Build a `MethodCtorInfo` from any `MethodBase` with `new MethodCtorInfo(mb)` or `MethodCtorInfo.TryNew(mb, out var mci)`.

Mark a constructor or factory `[AreReadable]` to have the engine register **all of its parameter types** too, so a whole object graph becomes mappable from one entry point.

## Generic types

By default a generic type is registered as its **generic definition** (`Result<>`, not `Result<int>`), so one configuration applies to every closed form. When resolving, the engine prefers an exact closed-type entry if one exists, otherwise it closes the definition's discovered methods to the needed arguments.

Rules for generic entry points.

- A **generic method** may be added only if its generic arguments match the target's exactly (order and count).
- The **declaring type cannot be generic**. The engine needs a concrete host to resolve the call.

```csharp
public class DataWrapper<T> { public DataWrapper(T value) { } }

public static class WrapperFactory {                       // VALID, non-generic host
    public static DataWrapper<T> Create<T>(T value) => new(value);
}
public static class GenericFactory<T> {                    // INVALID, generic host
    public static DataWrapper<T> Create(T value) => new(value);
}
```

## Post-construction members

`AvailableMembers` (`ReadOnlySpan<MemberParser>`) is the set filled after construction. Public fields (excluding `readonly` and `const`) and properties with a public setter (`init` excluded). You can add members by hand, including private ones or **external static setters**, a `static` method taking `(instance, value)`, on a non-generic class, generic-aligned with the target.

```csharp
var method  = typeof(ExternalLogic).GetMethod("SetSecretCode"); // static (UserProfile, string)
var matcher = ParamInfo.TryNew(method.GetParameters()[1]);
var manual  = new MemberParser(method, matcher);
```

**Overwrite risk.** Post-construction runs after the constructor, so if a member and a constructor parameter map to the same column, the member wins. Prevent it by making the member non-public or non-writable (so it is not discovered) or by removing it from `AvailableMembers`.
