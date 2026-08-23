# Name adaptation

## Case insensitive names

```csharp
public record Customer(int Id, string Name);

Customer customer = cnn.Query<Customer>("SELECT CustomerId AS ID, Name AS name FROM customers WHERE CustomerId = @customerId", new { customerId = 12 });
// ID fills Id.
// name fills Name.
```

## SQL alias

```csharp
public record Customer(int Id, string Name);

List<Customer> customers = cnn.Query<List<Customer>>("SELECT customer_id AS Id, display_name AS Name FROM customers");
```

## Alt

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);

List<Customer> customers = cnn.Query<List<Customer>>("SELECT customer_id, display_name FROM customers");
```

The declared member name remains accepted alongside its alternate name.

## External name mapping

```csharp
public record Customer(int Id, string Name);

TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch
    {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });

List<Customer> customers = cnn.Query<List<Customer>>("SELECT customer_id, display_name FROM customers");
```

SQL keeps `customer_id` and `display_name`. The CLR type keeps `Id` and `Name`. The registration carries the translation.

## Paths compose with names

```csharp
public record Address([Alt("Postal")] int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);

Person person = cnn.Query<Person>("SELECT PersonId AS Id, ZipCode AS HomePostal, City AS HomeCity FROM people WHERE PersonId = @personId", new { personId = 12 });
// HomePostal reaches Home.Zip through Alt("Postal").
```

The same process keeps going when the same type appears again.

```csharp
public record Employee(int Id, string Name, [Alt("Boss")] Employee? Manager = null) : IDbReadable;

Employee employee = cnn.Query<Employee>("SELECT e.EmployeeId AS Id, e.Name, m.EmployeeId AS ManagerId, m.Name AS ManagerName, b.EmployeeId AS ManagerBossId, b.Name AS ManagerBossName FROM employees e LEFT JOIN employees m ON m.EmployeeId = e.ManagerId LEFT JOIN employees b ON b.EmployeeId = m.ManagerId WHERE e.EmployeeId = @employeeId", new { employeeId = 12 });
// ManagerBossId reaches employee.Manager.Manager.Id.
```

[Recursive mapping](nesting.md) · [Construction paths](construction-paths.md)

## Skip fixed path segments

```csharp
public record Inner([AltSkippingSegments("Code", 2)] int Code) : IDbReadable;
public record Middle(Inner Sub) : IDbReadable;
public record Outer(int Id, Middle Mid);
```

```text
MidSubCode    complete path
MidCode       accepted alternate path
```

## Skip through a named path segment

```csharp
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third) : IDbReadable;
```

`AltUpTo` changes the accepted path through the named segment.

[AltUpTo API](xref:Rinku.Mapping.AltUpToAttribute)

## No name match

```csharp
public readonly record struct Boxed<T>([NoName] T Value);

Boxed<int> value = cnn.Query<Boxed<int>>("SELECT COUNT(*) FROM albums");
```

`[NoName]` accepts the next compatible column without matching a column name.

[Reading order](reading-order.md) · [Type registration](../customization/type-registration.md)
