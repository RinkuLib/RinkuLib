# Reading order

Normal object mapping searches unused columns by name. Sequential shapes such as tuples move through the row from left to right.

## Normal objects search by name

Column order and unrelated gaps do not matter for a normal object.

```csharp
public record Person(int Id, string Name, string? Email = null);

static readonly QueryCommand GetPerson = new("SELECT Name, LastLogin AS Note, PersonId AS Id FROM people WHERE PersonId = @personId");

Person person = GetPerson.Query<Person>(cnn, new { personId = 1 });
```

```text
Name | Note | Id
```

`Name` and `Id` are filled by name. `Note` is unused. `Email` keeps its default because no matching column exists.

A required slot with no matching column makes the construction path unusable.

```csharp
public record Person(int Id, string Name, string Email);

Person person = GetPerson.Query<Person>(cnn, new { personId = 1 });
// RINKU3001 because Email is required.
```

## Tuples move left to right

Each tuple element starts after the columns claimed by the previous element.

```csharp
public record Order(int Id, decimal Total) : IDbReadable;
public record Customer(int Id, string Name, string? Email = null) : IDbReadable;

static readonly QueryCommand GetOrderWithCustomer = new("SELECT o.OrderId AS Id, o.Total, c.CustomerId AS Id, c.Name, c.Email FROM orders o JOIN customers c ON c.CustomerId = o.CustomerId WHERE o.OrderId = @orderId");

(Order order, Customer customer) = GetOrderWithCustomer.Query<(Order, Customer)>(cnn, new { orderId = 1 });
```

```text
Id | Total | Id | Name | Email
```

`Order` claims the first `Id` and `Total`. `Customer` begins at the second `Id`.

This boundary is why the same mapped type can appear twice in one tuple.

## Require the next column

`[CanNotLookAnywhere]` prevents a slot from searching past the next available column.

```csharp
public record Entry(int Id, [CanNotLookAnywhere] int? Code = null);
```

```text
Id | Other | Code
```

`Id` is filled. `Code` checks `Other`, sees that it does not match, and keeps its default. It does not skip ahead to the later `Code` column.

This is useful when a slot marks a boundary between repeated shapes.

## Search past a gap in a sequential shape

Sequential slots normally stay within their current position. `[CanLookAnywhere]` lets one slot search later unused columns.

```csharp
public record struct Person(int Id, string Name) : IDbReadable;
public record struct Address([CanLookAnywhere] int Zip, string City) : IDbReadable;

static readonly QueryCommand GetPersonAddress = new("SELECT PersonId AS Id, Name, AddressNote AS Note, PostalCode AS Zip, City FROM people WHERE PersonId = @personId");

(Person person, Address address) = GetPersonAddress.Query<(Person, Address)>(cnn, new { personId = 1 });
```

```text
Id | Name | Note | Zip | City
```

`Address.Zip` skips `Note`. `Address.City` continues after `Zip`.

## Reuse a column

`[MayReuseCol]` reads a column without marking it consumed. A later slot can read the same column again.

```csharp
public record Price([Alt("Amount"), MayReuseCol] decimal Original, decimal Amount);

Price price = GetPrice.Query<Price>(cnn);
```

```text
Amount
12.50
```

Both `Original` and `Amount` receive `12.50`.

Without `[MayReuseCol]`, the first slot consumes `Amount` and the second required slot cannot use it.

## Apply a rule to a nested value

A reading-order attribute on a nested member changes where that nested value begins. Its `Subtree` form applies the rule to every slot inside the nested value.

```csharp
public record Coordinates(int X, int? Y = null) : IDbReadable;
public record Location(int Id, [CanLookAnywhereSubtree] Coordinates Position);
```

```text
Id | Note | PositionX | Gap | PositionY
```

`Position.X` skips `Note`, and `Position.Y` skips `Gap` because the rule applies to the complete subtree.

With plain `[CanLookAnywhere]` on `Position`, only the beginning of the nested value can move. Its later slots return to their own normal rules.

`[CanNotLookAnywhereSubtree]` and `[MayReuseColSubtree]` apply their matching behavior to every nested slot in the same way.

## Combine name and order rules

Name attributes still decide which columns are acceptable. Reading-order attributes decide where those names may be searched and whether a match is consumed.

```csharp
public record Product([Alt("ProductId"), CanLookAnywhere] int Id, string Name);
```

`Id` may search later columns for either `Id` or `ProductId`.

## Configure rules at startup

The same behavior can be applied to generated slots during application setup when the model cannot carry attributes.

```csharp
ParamInfo.RegistrationInitializer = static slot => string.Equals(slot.NameComparer.GetDefaultName(), "Id", StringComparison.OrdinalIgnoreCase)
    ? slot.WithColModifier(FlagUpdater.RemoveSequentialRead)
    : slot;
```

This example lets every generated `Id` slot search later columns. Configure the initializer before parsers are created. Existing cached parsers keep the rules used when they were built.

[Tuples](tuples.md) covers sequential result shapes. [Names](names.md) covers accepted names and prefixes. [Slot rules](../customization/slot-rules.md) covers runtime updates and custom column-usage attributes.
