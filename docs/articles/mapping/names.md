# Adapt names

Names match without regard to case.

```csharp
public record Customer(int Id, string Name);

Customer customer = GetCustomer.Query<Customer>(cnn);
// ID | name -> Customer.Id | Customer.Name
```

A nested value adds its member name to the names inside it.

```csharp
public record Address(int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);

Person person = GetPerson.Query<Person>(cnn);
// Id | HomeZip | HomeCity
```

When the code cannot change, adapt the SQL.

```csharp
public record Customer(int Id, string Name);
```

```sql
SELECT customer_id AS Id, display_name AS Name
FROM customers
```

When the SQL cannot change, add accepted names to the code.

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);
```

```sql
SELECT customer_id, display_name
FROM customers
```

`Alt` keeps the declared name as well as the additional name.

```csharp
static readonly QueryCommand GetCustomerWithAliases = new("""
    SELECT customer_id AS Id, display_name AS Name
    FROM customers
    """);

static readonly QueryCommand GetCustomerWithDatabaseNames = new("""
    SELECT customer_id, display_name
    FROM customers
    """);

Customer first = GetCustomerWithAliases.Query<Customer>(cnn);
Customer second = GetCustomerWithDatabaseNames.Query<Customer>(cnn);
```

An alternate name on a nested value still includes the outer prefix.

```csharp
public record Address([Alt("Postal")] int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);

Person person = GetPerson.Query<Person>(cnn);
// HomeZip or HomePostal can fill person.Home.Zip.
```

## Skip prefix parts

`[AltSkippingSegments]` removes a fixed number of inner prefix parts for its alternate name.

```csharp
public record Inner([AltSkippingSegments("Code", 2)] int Code) : IDbReadable;
public record Middle(Inner Sub) : IDbReadable;
public record Outer(int Id, Middle Mid);

Outer value = GetOuter.Query<Outer>(cnn);
// MidSubCode uses the full name.
// MidCode uses the alternate name after skipping Sub.
```

`[AltUpTo]` removes prefix parts through a named part of the path.

```csharp
public record LayerOne(int First, LayerTwo Two);
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third) : IDbReadable;

LayerOne value = GetLayers.Query<LayerOne>(cnn);
// First | NotTooDeep | SuperDeep
```

## Ignore the name

`[NoName]` takes the next compatible column without requiring a name match.

```csharp
public readonly record struct Boxed<T>([NoName] T Value);

Boxed<int> value = GetNumber.Query<Boxed<int>>(cnn);
// Any available integer column can fill Value.
```

When neither the SQL nor the type should carry the rule, configure it at startup.

```csharp
TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });
```

The mapping can now read the fixed SQL into the unchanged type.

```csharp
Customer customer = GetCustomer.Query<Customer>(cnn);
// customer_id | display_name -> Customer.Id | Customer.Name
```

See [mapping slot rules](../customization/slot-rules.md) for more ways to change names at runtime.

Continue with [tuple mapping](tuples.md).
