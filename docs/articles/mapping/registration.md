# Registration

Every type parses through its parsing info, the entry a registry keeps per type. This page covers how a type gets its entry and what registering can decide. What the entry holds is on [construction paths](construction-paths.md), and the slot-level rules live with their concepts, [nullability](nullability.md), [names](names.md), [reading order](reading-order.md).

## Application-wide registration defaults

Registration is lazy, so there is no point at which every application's type has already been discovered. Install
registration initializers during application startup instead. Each initializer receives the mutable metadata Rinku has
just created and can leave it alone or change it before registration publishes it.

`ParamInfo.RegistrationInitializer` runs after the attributes, name comparer, null handler, usage flags, and any custom
`IParamInfoMaker` have produced the final slot metadata:

```csharp
ParamInfo.RegistrationInitializer = static slot => {
    if (string.Equals(slot.NameComparer.GetDefaultName(), "Id", StringComparison.OrdinalIgnoreCase))
        slot.SetAbortOnNull(true);
};
```

There is no precedence policy hidden in the initializer. The callback sees the final result and decides whether to
preserve or replace any choice, including one produced by an attribute or custom maker.

`MethodCtorInfo.RegistrationInitializer` does the same for automatically built construction paths, and
`TypeParsingInfo.RegistrationInitializer` receives both the requested type and the default-factory-created metadata
before the registry publishes it:

```csharp
MethodCtorInfo.RegistrationInitializer = static path => path.Flags |= MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers;

TypeParsingInfo.RegistrationInitializer = static (type, info) => {
    if (info is ICanUpdateGroupKey grouping && type.GetProperty("Id") is { } id)
        grouping.GroupKey = new EqualityGroupingRule(id);
};
```

The callbacks run only while registration metadata is being created, never while a row is read or a command is run.
Configure them before concurrent query use. Metadata already created is deliberately not revisited; every type lazily
created afterward observes the current initializer. Changes made to the metadata supplied to an initializer are part
of that object's construction and do not invalidate parsers built from previously published metadata. An initializer
should therefore configure the object it receives rather than use its registration scope to mutate unrelated existing
metadata.

Direct construction remains the lower-level escape hatch. `new ParamInfo(...)` and `new MethodCtorInfo(...)` bypass
their initializers. A `TypeParsingInfo` explicitly supplied to `GetOrAdd` or `AddOrSet` likewise bypasses the type
initializer. `MethodCtorInfo.TryNew`, which automatic discovery uses, does invoke the construction initializer. The
one-argument `MethodCtorInfo` constructor still creates its parameters through `ParamInfo.Create`; pass an explicitly
built `ParamInfo[]` as well when the entire path must bypass both conventions.

## How a type gets its info

Querying a type registers it, whatever the `T`:

```csharp
Album album = GetAlbum.Query<Album>(cnn);   // Album registers on first use
```

Basic types and enums, anything a `DbDataReader` exposes directly, work on contact. Any other type must be known before the engine will consider it inside another one. A custom type reached only as a slot, with no registration, makes its construction path unsatisfiable. There are three ways to make it known.

The `IDbReadable` marker, registering the type wherever it appears:

```csharp
public record Artist(int Id, string Name) : IDbReadable;
public record Album(int Id, string Title, Artist Artist);   // Artist resolves as a nested slot
```

`[AreReadable]` on a constructor or factory, registering its parameter types:

```csharp
[method: AreReadable]
public record Invoice(int Id, Customer Customer, Address Shipping);
// Customer and Address register along with Invoice
```

Generic parsing info registers its own generic arguments when the caller asks for parameter registration. This is one level at a time:

```csharp
[method: AreReadable]
public record Report(int Id, KeyValuePair<Customer, Address> Pair);
// Report registers KeyValuePair<Customer, Address>.
// KeyValuePair's parsing info registers Customer and Address.
// A generic type nested inside Customer is not walked automatically.
```

And manually:

```csharp
var info = TypeParsingInfo.GetOrAdd<Address>();
```

If a target needs custom scalar behavior, give it a parsing-info implementation just like any other custom
shape. `ScalarTypeParsingInfo<T>` supplies the ordinary name, ordinal, sequential-read, reuse, and fallback
negotiation. The implementation only selects compatible source columns and returns the plan that emits the
read:

```csharp
public readonly record struct RegisteredDate(DateTime Value) : IDbReadable;

sealed class RegisteredDateInfo : ScalarTypeParsingInfo<RegisteredDate>
{
    static readonly MethodInfo ConvertMethod = typeof(RegisteredDateInfo).GetMethod(nameof(Convert), BindingFlags.Static | BindingFlags.NonPublic)!;

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal)
    {
        if (column.Type != typeof(DateTime))
            return null;
        ITypeConverter converter = new MethodCallConverter(ConvertMethod);
        if (Nullable.GetUnderlyingType(targetType) is not null)
            converter = new NullableWrapperConverter(converter);
        return new ConvertedScalarPlan(parentType, converter, parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal);
    }

    static RegisteredDate Convert(DateTime value) => new(value.AddDays(1));
}

TypeParsingInfo.AddOrSet(typeof(RegisteredDate), new RegisteredDateInfo());
```

The same entry handles a top-level result, a constructor parameter, a nested member, and nullable
`RegisteredDate?`. `ConvertedScalarPlan` uses the standard typed `DbDataReader` call and emits the selected
`ITypeConverter` directly into the generated parser. There is no converter lookup during row parsing.

When a provider needs its own reader API, the parsing implementation returns its own plan instead. The plan
inherits the same null and ordinal behavior but emits the provider read directly:

```csharp
sealed class PostgresIntArrayInfo : ScalarTypeParsingInfo<int[]>
{
    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal)
        => column.Type == typeof(Array) ? new PostgresIntArrayPlan(parentType, parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal) : null;
}

sealed class PostgresIntArrayPlan(Type parentType, string parameterName, INullColHandler nullHandler, int ordinal)
    : ScalarDbItemPlan<int[]>(parentType, parameterName, nullHandler, ordinal)
{
    static readonly MethodInfo ReadMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!.MakeGenericMethod(typeof(int[]));

    protected override void EmitValue(ColumnInfo column, Generator generator, out object? targetObject)
    {
        targetObject = null;
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, ColumnOrdinal);
        generator.Emit(OpCodes.Callvirt, ReadMethod);
    }
}

TypeParsingInfo.AddOrSet(typeof(int[]), new PostgresIntArrayInfo());
```

The generated parser calls `GetFieldValue<int[]>` directly: there is no runtime registry or interface dispatch.
Remove the ordinary type registration with `TypeParsingInfo.TryRemove(typeof(int[]), out _)`. Generated parsers
are managed independently through the parser invalidation API, as with every other parsing-info change.

Separate from the rules, each type parsing implementation decides what its own generic arguments mean. `List<TInner>` registers `TInner` when its caller passes `[AreReadable]`; it does not walk generic arguments inside `TInner`. A custom `TypeParsingInfo` can make the same decision in its own implementation.

## Generic types

`GetOrAdd` saves a generic type under its definition by default, so one entry (`Result<>`) covers every closed form. Pass `saveAsGenericDefinitionWhenGeneric: false` to register a single closed form (`Result<int>`) with its own entry instead. Resolution takes the exact closed entry when one exists and falls back to the definition otherwise, so both can coexist. Configure the definition for the general case, a closed form for the exception.

```csharp
var forAll  = TypeParsingInfo.GetOrAdd(typeof(Result<>));                          // every Result<T>
var forInts = TypeParsingInfo.GetOrAdd<Result<int>>(saveAsGenericDefinitionWhenGeneric: false); // just Result<int>
```

Registering a generic *method* as a construction path for such a type is on [construction paths](construction-paths.md#generic-factories).

## Registering with another info

Registration also decides which parsing implementation handles the type. A constructor-position implementation
can map by position and type alone, ignoring names and using [sequential reading](reading-order.md).

For example, a custom implementation can make this shape read two consecutive `double` columns:

```csharp
public record struct Coordinates(double Lat, double Long);

// Columns: Longitude | Latitude
// The registered implementation maps the first value to Lat and the second to Long.
```

With several constructors, `[DbConstructor]` marks the one to use. Without it, the first constructor that takes parameters wins.

```csharp
public class Segment {
    public Segment(int start) { }
    [DbConstructor] public Segment(int start, int end) { }   // the marked constructor is used
}
```

Some built-in shapes use specialized parsing implementations. `ValueTuple` is the name-ignoring,
constructor-position example described in [tuple mapping](../running-queries/result-shapes.md#tuples), while
[`DynaObject`](dynaobject.md) has a generated dynamic shape and `Dictionary<string, object>` has a
schema-adaptive runtime shape. Both are ordinary `TypeParsingInfo` registrations. You can write and register
your own implementation when the built-in rules do not fit.

### What an info supports

Registry helpers and refinements operate through capability interfaces. An implementation exposes only the
capabilities it supports; a helper returns `false` when the registered implementation does not expose that
capability. Configuration deliberately belonging to one implementation can still use that implementation's
API directly.

When an info does not implement a helper's interface, the helper returns `false` instead of throwing. So you match on the interface, never on a concrete type. Your own info can implement any of these interfaces, and the same helpers work on it just as they do on the default.

Register and configure before query use. Registration APIs change registration state only: they do not inspect,
remove, notify, or dispose generated parser caches. An existing parser object and every cache reference
to it remain alive. If an application deliberately changes registration after querying has begun and wants to
discard old cache entries, it must explicitly use the [parser cache API](parsers.md#invalidation).

Remove one exact metadata registry entry independently with `TypeParsingInfo.TryRemove(type, out var removed)`. The key is used exactly as supplied: removing a closed generic does not remove its open definition, and nullable normalization is not applied. Removal affects later metadata negotiation only. It neither changes the returned `TypeParsingInfo` object nor invalidates parsers already generated from it; invalidate those parsers separately when that is also intended.

## Replacing the shipped metadata implementation

The registry does not construct `DefaultTypeParsingInfo`, `BaseTypeInfo`, or the array implementation. It asks
the interface-typed `TypeParsingInfo.DefaultFactory` slot. Rinku installs its shipped factory during module
initialization, and an application can replace it during startup:

```csharp
TypeParsingInfo.DefaultFactory = new MyTypeParsingInfoFactory();
```

Implement `ITypeParsingInfoFactory` to provide scalar and array entries and to create an entry for an ordinary
type. The registry still owns lookup, exact-versus-open-generic precedence, and thread-safe publication; the
factory owns only the implementation being registered. The factory is consulted
for missing entries. Existing entries and explicit `TypeParsingInfo.AddOrSet` registrations stay in the
registry, so replacing the default is not all-or-nothing.

`DefaultTypeParsingInfoFactory` is public when only one part needs changing: wrap it and delegate the members
you want to keep. Configure the slot before concurrent query use.
