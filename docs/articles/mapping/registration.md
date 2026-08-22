# Registration

A root result type is an explicit request.

```csharp
Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Nested types must be readable before their construction paths can participate.

## Register from the type

Implement `IDbReadable` on a nested type.

```csharp
public record Customer(int Id, string Name) : IDbReadable;
public record Invoice(int Id, Customer Customer);

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Without the marker or another registration, the nested `Customer` path is unavailable.

## Register construction parameter types

Use `[AreReadable]` on a constructor or factory when its parameter types should become readable.

```csharp
public record Customer(int Id, string Name);

[method: AreReadable]
public record Invoice(int Id, Customer Customer);
```

Use `[AreReadable]` on the type when the rule should apply to its immediate construction parameters and writable members.

```csharp
[AreReadable]
public record Invoice(int Id, Customer Customer);
```

The rule applies one level at a time.

## Register during application setup

```csharp
TypeParsingInfo.GetOrAdd<Customer>();
```

Do setup registration before queries start. Parsers already built from older registration state keep their existing behavior.

## Registration does not guarantee a usable path

```csharp
public sealed class Attachment : IDbReadable
{
    public Attachment(int id, string name) { }
    public Attachment(int id, Stream content) { }
}
```

Registration makes the type available. A specific construction path still needs columns and readable slot types that can satisfy it.

## Application wide conventions

Use [advanced type registration](../customization/type-registration.md) when the application should change mapping defaults without attributes on every model.
