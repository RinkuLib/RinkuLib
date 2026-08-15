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

Private constructors and factories on another non-generic class can be added the same way.

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

`ICanProvideMembers.AvailableMembers` can replace the member set or its order.

[Map a nested object](nesting.md).
