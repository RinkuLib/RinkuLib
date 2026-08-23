# Reading order

## Named object slots

```csharp
public record Person(int Id, string Name, string? Email = null);

static readonly QueryCommand GetPerson = new("SELECT Name, LastLogin AS Note, PersonId AS Id FROM people WHERE PersonId = @personId");

Person person = GetPerson.Query<Person>(cnn, new { personId = 12 });
// Name and Id are found by name.
// Note remains unused.
// Email keeps its default.
```

A required slot with no matching column makes that construction path unusable.

[Construction paths](construction-paths.md)

## Sequential tuple slots

```csharp
public record Order(int Id, decimal Total) : IDbReadable;
public record Customer(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetOrder = new("SELECT o.OrderId AS Id, o.Total, c.CustomerId AS Id, c.Name FROM orders o JOIN customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId = @orderId");

(Order order, Customer customer) = GetOrder.Query<(Order, Customer)>(cnn, new { orderId = 12 });
```

The first tuple slot claims its columns. The next slot continues from the remaining columns.

[Tuples](tuples.md)

## Require the next column

```csharp
public record Entry(int Id, [CanNotLookAnywhere] int? Code = null);
```

If the next unused column does not match `Code`, that slot does not search later columns.

## Search later from a sequential slot

```csharp
public record struct Address([CanLookAnywhere] int Zip, string City) : IDbReadable;
```

`Zip` may search later unused columns even when the containing shape is being read sequentially.

## Reuse a column

```csharp
public record Entry([MayReuseCol] int Id, int CopyOfId);
```

The `Id` slot can read a column without marking it consumed, so a later compatible slot can reuse it.

## Apply a rule to the complete subtree

```csharp
public record Address(int Zip, string City) : IDbReadable;
public record Person(int Id, [CanLookAnywhereSubtree] Address Address);
```

`CanLookAnywhereSubtree` lets every slot inside `Address` search later unused columns. `CanLookAnywhere` changes only the first claim made by the nested value.

```csharp
public record Person(int Id, [CanNotLookAnywhereSubtree] Address Address);
```

`CanNotLookAnywhereSubtree` keeps sequential reading through the complete nested value.

```csharp
public record Person(int Id, [MayReuseColSubtree] Address Address);
```

`MayReuseColSubtree` keeps every column claimed by `Address` reusable.

[Custom slot rules](../customization/slot-rules.md)
