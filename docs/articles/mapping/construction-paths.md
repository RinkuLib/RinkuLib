# Construction paths

Each constructor or static factory that can build the requested type is a construction path.

```csharp
public interface IShape {
    public static IShape FromCircle(double radius) => new Circle(radius);

    public static IShape FromRectangle(double width, double height) => new Rectangle(width, height);
}

public record Circle(double Radius) : IShape;
public record Rectangle(double Width, double Height) : IShape;
```

The returned columns select a satisfiable path.

```text
Radius          -> FromCircle(double)
Width | Height  -> FromRectangle(double, double)
```

## Selection order

The first satisfiable path wins. The default registration attempts to place richer compatible paths before simpler ones.

```csharp
public interface Payment {
    public static Payment Create(string cardNumber) => new Card(cardNumber);

    public static Payment Create(string cardNumber, string owner) => new NamedCard(cardNumber, owner);
}

public record Card(string CardNumber) : Payment;
public record NamedCard(string CardNumber, string Owner) : Payment;
```

```text
CardNumber          -> Card
CardNumber | Owner  -> NamedCard
```

Do not depend on inferred ordering when one path must always win. Select it explicitly or replace the path order.

## Select one constructor with CtorTypeInfo

`[DbConstructor]` tells `CtorTypeInfo` which constructor to use.

```csharp
public sealed class User {
    public User(int id, string name) { }

    [DbConstructor]
    public User(int id) { }
}

TypeParsingInfo.AddOrSet<User>(CtorTypeInfo.Instance);
```

```text
Id | Name -> User(int id)
```

The marked constructor remains selected even when another constructor appears first.

## Add a constructor or factory

`AddPossibleConstruction` accepts a constructor or static factory whose result can be assigned to the target type.

```csharp
ConstructorInfo constructor = typeof(ExternalPayment).GetConstructor([typeof(int)]) ?? throw new InvalidOperationException("Constructor was not found.");

TypeParsingInfo.GetOrAdd<IPayment>().AddPossibleConstruction(constructor);
```

### Private constructor

Use explicit binding flags when the constructor is not public.

```csharp
ConstructorInfo privateConstructor = typeof(PrivatePayment).GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance,
    binder: null,
    types: [typeof(int)],
    modifiers: null) ?? throw new InvalidOperationException("Constructor was not found.");

TypeParsingInfo.GetOrAdd<IPayment>().AddPossibleConstruction(privateConstructor);
```

### Discover private paths and members

`[UsePrivateMembers]` includes non-public constructors, static factories, fields, properties, and setters in automatic discovery.

```csharp
[UsePrivateMembers]
public sealed class PrivateInvoice {
    [CanCompleteWithMembers]
    private PrivateInvoice(int id) => Id = id;

    public int Id { get; }
    private string Note { get; set; } = "";

    public string ReadNote() => Note;
}

PrivateInvoice invoice = GetPrivateInvoice.Query<PrivateInvoice>(cnn);
```

The attribute initializes the same flag that application setup can set directly.

```csharp
if (TypeParsingInfo.GetOrAdd<PrivateInvoice>() is DefaultTypeParsingInfo info)
    info.Flags |= DefaultTypeParsingFlags.UsePrivateMembers;
```

Set discovery flags before the type is first parsed. Changing them later does not rebuild construction paths or members already discovered.

### Factory on another class

An external static factory can also be registered.

```csharp
MethodInfo factory = typeof(PaymentFactory).GetMethod(
    nameof(PaymentFactory.Create),
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string)],
    modifiers: null) ?? throw new InvalidOperationException("Factory method was not found.");

TypeParsingInfo.GetOrAdd<IPayment>().AddPossibleConstruction(factory);
```

## Add an open generic factory

Register a generic factory on an open target type.

```csharp
public static class BoxFactory {
    public static Box<T> Create<T>(T value) => new(value);
}

MethodInfo factory = typeof(BoxFactory).GetMethod(nameof(BoxFactory.Create)) ?? throw new InvalidOperationException("Factory method was not found.");

TypeParsingInfo.GetOrAdd(typeof(Box<>)).AddPossibleConstruction(factory);
```

```csharp
Box<int> number = GetNumber.Query<Box<int>>(cnn);
Box<string> text = GetText.Query<Box<string>>(cnn);
```

The factory method’s type arguments must match the returned type arguments in the same order.

## Replace path order

`ICanProvideConstructions` exposes the complete path set.

```csharp
if (TypeParsingInfo.GetOrAdd<UserProfile>() is ICanProvideConstructions info) {
    MethodCtorInfo[] paths = info.PossibleConstructors.ToArray();
    Array.Sort(paths, (left, right) => right.Parameters.Length - left.Parameters.Length);
    info.PossibleConstructors = paths;
}
```

The example makes paths with more parameters win first.

## Configure one path

`GetConstruction` selects a path by parameter types.

```csharp
MethodCtorInfo path = TypeParsingInfo.GetOrAdd<UserProfile>().GetConstruction(typeof(int), typeof(string));

path.GroupKey = new EqualityGroupingRule(["Id"]);
path.Flags |= MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers;
path.Parameters[0].UpdateAltName(_ => new NameComparer("UserId"));
path.Parameters[1].SetAbortOnNull(true);
```

Use `GetConstruction(factoryMethod)` when paths share the same parameter types.

```csharp
public interface IImportResult {
    public static IImportResult Accepted(string message) => new AcceptedImport(message);
    public static IImportResult Rejected(string message) => new RejectedImport(message);
}

public sealed record AcceptedImport(string Message) : IImportResult;
public sealed record RejectedImport(string Message) : IImportResult;

MethodInfo rejectedFactory = typeof(IImportResult).GetMethod(nameof(IImportResult.Rejected))
    ?? throw new InvalidOperationException("Factory method was not found.");

MethodCtorInfo rejectedPath = TypeParsingInfo.GetOrAdd<IImportResult>().GetConstruction(rejectedFactory);
rejectedPath.Parameters[0].UpdateAltName(_ => new NameComparer("ErrorMessage"));
```

Both factories take one `string`, so selecting only by parameter types cannot identify the rejected path.

## Supply a custom fallback

CLR optional-argument values are not arbitrary mapping fallbacks. A missing `Rating` cannot use the `5` in this declaration by itself.

```csharp
public record RatedAlbum(int Id, int Rating = 5);

RatedAlbum album = GetAlbumIdOnly.Query<RatedAlbum>(cnn);
// RINKU3001 because Rating has no matching column.
```

An `IFallbackParserGetter` can provide a read plan for the missing slot.

```csharp
sealed class RatingFallback : SimpleDbItemParser, IFallbackParserGetter {
    public static RatingFallback Instance { get; } = new();

    public DbItemPlan? FallbackTryGetParser(Type type) => type == typeof(int) ? this : null;

    public override void Emit(ColumnInfo[] columns, Rinku.Mapping.Emission.Generator generator, NullSetPoint nullSetPoint)
        => generator.Emit(OpCodes.Ldc_I4_5);

    public override bool IsSequencial(ref int previousIndex) => true;
    public override bool NeedNullSetPoint(ColumnInfo[] columns) => false;
}

MethodCtorInfo path = TypeParsingInfo.GetOrAdd<RatedAlbum>().GetConstruction(typeof(int), typeof(int));
ParamInfo rating = path.Parameters[1];
path.Parameters[1] = new ParamInfoPlus(
    rating.Type,
    rating.NullColHandler,
    rating.NameComparer,
    IColModifier.Nothing,
    RatingFallback.Instance);

RatedAlbum album = GetAlbumIdOnly.Query<RatedAlbum>(cnn);
// RatedAlbum(12, 5)
```

## Add a member after construction

`AddMember` accepts a field, property, setter, or external static setter.

```csharp
public sealed class SecretHolder : IDbReadable {
    public int Id { get; set; }
    public string? Secret { get; private set; }

    public static void SetSecret(SecretHolder target, string secret) => target.Secret = secret;
}

MethodInfo setter = typeof(SecretHolder).GetMethod(nameof(SecretHolder.SetSecret)) ?? throw new InvalidOperationException("Setter was not found.");

TypeParsingInfo.GetOrAdd<SecretHolder>().AddMember(setter);
```

## Replace the available members

`ICanProvideMembers.AvailableMembers` accepts the complete ordered member set. This setup removes `DebugNote` so it is never considered for post-construction mapping.

```csharp
if (TypeParsingInfo.GetOrAdd<ImportRow>() is ICanProvideMembers members) {
    members.AvailableMembers = members.AvailableMembers
        .ToArray()
        .Where(item => item.Member.Name != nameof(ImportRow.DebugNote))
        .ToArray();
}
```

Assign a reordered array instead when member priority must change.

[Map a nested object](nesting.md).
