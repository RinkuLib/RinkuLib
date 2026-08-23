# Mapping registration

## Explicit root result

```csharp
public record Invoice(int Id, decimal Total);

Invoice invoice = cnn.Query<Invoice>("SELECT InvoiceId AS Id, Total FROM invoices WHERE InvoiceId = @invoiceId", new { invoiceId = 12 });
```

The root type is explicit in the requested result shape.

## Type reached through the shape

```csharp
public record Customer(int Id, string Name) : IDbReadable;
public record Invoice(int Id, Customer Customer);

Invoice invoice = cnn.Query<Invoice>("SELECT i.InvoiceId AS Id, c.CustomerId AS CustomerId, c.Name AS CustomerName FROM invoices i JOIN customers c ON c.CustomerId = i.CustomerId WHERE i.InvoiceId = @invoiceId", new { invoiceId = 12 });
```

`Customer` participates as a nested mapping through `IDbReadable`.

The same registration can live in setup code.

```csharp
public record Customer(int Id, string Name);

TypeParsingInfo.GetOrAdd<Customer>();
```

## AreReadable

```csharp
public record Customer(int Id, string Name);

[method: AreReadable]
public record Invoice(int Id, Customer Customer);
```

The construction parameter types for that construction become readable.

A type-level attribute applies to immediate construction parameters and writable members.

```csharp
[AreReadable]
public record Invoice(int Id, Customer Customer);
```

Mapping continues recursively from there. Each later type participates through its own registration and construction.

[Recursive mapping](nesting.md)

## Registration and construction are separate

```csharp
public sealed class Attachment : IDbReadable
{
    public Attachment(int id, string name) { }
    public Attachment(int id, Stream content) { }
}
```

The type is readable. A particular construction still needs a returned shape that satisfies its inputs.

[Construction paths](construction-paths.md)

## Setup timing

```csharp
TypeParsingInfo.GetOrAdd<Customer>();
```

Parsers already built from earlier registrations keep their existing mapping until invalidated.

[Cache control](../customization/caches.md) · [Type registration](../customization/type-registration.md)
