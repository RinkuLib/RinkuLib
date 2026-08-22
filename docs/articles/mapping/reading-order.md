# Reading order

Normal objects search unused columns by name.

```csharp
public record Person(int Id, string Name, string? Email = null);

static readonly QueryCommand GetPerson = new("SELECT Name, LastLogin AS Note, PersonId AS Id FROM people WHERE PersonId = @personId");

Person person = GetPerson.Query<Person>(cnn, new { personId = 1 });
```

`Name` and `Id` are found even though they are not beside each other. `Note` stays unused. `Email` keeps its default.

A required slot with no matching column makes that construction path unusable.

## Tuples read from left to right

```csharp
public record Order(int Id, decimal Total) : IDbReadable;
public record Customer(int Id, string Name) : IDbReadable;

static readonly QueryCommand GetOrder = new("SELECT o.OrderId AS Id, o.Total, c.CustomerId AS Id, c.Name FROM orders o JOIN customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId = @orderId");

(Order order, Customer customer) = GetOrder.Query<(Order, Customer)>(cnn, new { orderId = 1 });
```

The first object claims its columns. The next tuple element begins after those consumed columns.

See [tuples](tuples.md) for positional result shapes that use the same reading order rules.

## Require the next column

Use `[CanNotLookAnywhere]` when a slot must not search past the next available column.

```csharp
public record Entry(int Id, [CanNotLookAnywhere] int? Code = null);
```

If the next unused column does not match `Code`, the slot keeps its default instead of searching later columns.

## Search later columns in a sequential shape

Use `[CanLookAnywhere]` when one sequential slot may search later unused columns.

```csharp
public record struct Address([CanLookAnywhere] int Zip, string City) : IDbReadable;
```

This is useful when a tuple element contains an unrelated gap before its first matching column.

## Reuse a column

Use `[MayReuseCol]` when a slot may read a column without marking it consumed.

```csharp
public record Entry([MayReuseCol] int Id, int CopyOfId);
```

A later compatible slot can then use the same column.

These attributes affect usage behavior only. Use [slot rule customization](../customization/slot-rules.md) when an application needs a new rule instead of the built in choices.
