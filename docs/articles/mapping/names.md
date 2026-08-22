# Adapt names

Names match without regard to case.

```csharp
public record Customer(int Id, string Name);

Customer customer = GetCustomer.Query<Customer>(cnn);
// ID and name can fill Id and Name.
```

## Adapt the SQL

Alias columns when SQL is the cleanest place to express the result shape.

```sql
SELECT customer_id AS Id, display_name AS Name FROM customers
```

This keeps the C# type unchanged.

## Adapt the .NET side

Use `[Alt]` when the returned database names should stay unchanged.

```csharp
public record Customer([Alt("customer_id")] int Id, [Alt("display_name")] string Name);
```

The declared names still work too.

## Nested prefixes

Nested values normally include every member name in their column prefix.

```csharp
public record Address(int Zip, string City) : IDbReadable;
public record Person(int Id, Address Home);
```

```text
Id
HomeZip
HomeCity
```

An alternate name inside the nested type still keeps the outer prefix.

```csharp
public record Address([Alt("Postal")] int Zip, string City) : IDbReadable;
// HomeZip and HomePostal can both fill Zip.
```

## Skip prefix parts

Use `[AltSkippingSegments]` when an alternate name should remove a fixed number of inner path segments.

```csharp
public record Inner([AltSkippingSegments("Code", 2)] int Code) : IDbReadable;
public record Middle(Inner Sub) : IDbReadable;
public record Outer(int Id, Middle Mid);

// MidSubCode is the normal full name.
// MidCode is the alternate name.
```

Use `[AltUpTo]` when the alternate path should remove segments through a named part.

```csharp
public record LayerTwo([AltUpTo("NotTooDeep", "Two")] int Second, LayerThree Three) : IDbReadable;
public record LayerThree([AltUpTo("SuperDeep", "Two")] int Third) : IDbReadable;
```

## Ignore the column name

Use `[NoName]` when the next compatible column can fill the slot without a name match.

```csharp
public readonly record struct Boxed<T>([NoName] T Value);
```

## Keep the rule outside both sides

```csharp
TypeParsingInfo.GetOrAdd<Customer>().UpdateAltName(names =>
    names.GetDefaultName() switch
    {
        "Id" => new NameComparer("customer_id"),
        "Name" => new NameComparer("display_name"),
        _ => null
    });
```

Use setup registration when neither SQL nor model attributes should carry the database naming rule.

```csharp
// SQL keeps customer_id/display_name.
// Customer keeps Id/Name.
// Rinku owns only the translation between them.
```

See [advanced type registration](../customization/type-registration.md).
