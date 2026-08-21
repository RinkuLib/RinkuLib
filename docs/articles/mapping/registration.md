# Registration

This page covers registration in the default mapping system. A custom `TypeParsingInfo` or result parser can read a type another way.

A root result type is an explicit request and is registered for that use.

```csharp
Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Nested types must become registered before their construction paths can participate.

## Register automatically from the type

`IDbReadable` registers a type when it appears in a mapped path.

```csharp
public record Customer(int Id, string Name) : IDbReadable;
public record Invoice(int Id, Customer Customer);

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Without the marker, the nested `Customer` path is unavailable.

```csharp
public record Customer(int Id, string Name);
public record Invoice(int Id, Customer Customer);

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
// RINKU3001
```

## Register automatically from a construction path

`[AreReadable]` registers the parameter types used by that constructor or factory.

```csharp
public record Customer(int Id, string Name);

[method: AreReadable]
public record Invoice(int Id, Customer Customer);

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Another construction path does not make its own parameter types readable unless it carries the same declaration.

## Register direct types from a type

Place `[AreReadable]` on a type to apply the same rule to all of its immediate construction parameters and writable members.

```csharp
public record Customer(int Id, string Name);

[AreReadable]
public record Invoice(int Id, Customer Customer);

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

The attribute does not register `Invoice`; the root query does that. It keeps `Invoice`'s first-level paths available and permits their slot types, such as `Customer`, to be registered when parsing needs them. It does not copy the mark to the generated `Customer` registration.

Writable members use the same rule:

```csharp
public sealed class ShippingAddress {
    public string City { get; set; } = "";
}

[AreReadable]
public sealed class Shipment {
    public int Id { get; set; }
    public ShippingAddress Address { get; set; } = new();
}
```

To apply this convention to every default object mapping and continue it one type at a time, see [demand-driven registration](../customization/type-registration.md#enable-demand-driven-registration).

## Register manually during application setup

`GetOrAdd` performs manual registration during application setup.

```csharp
TypeParsingInfo.GetOrAdd<Customer>();

Invoice invoice = GetInvoice.Query<Invoice>(cnn);
```

Set registrations before queries start. Parsers already created from an earlier registration keep their existing behavior.

## Registration and construction paths

Registration makes a type available. Each construction path must still be satisfiable through registered types.

```csharp
public sealed class Attachment : IDbReadable {
    public Attachment(int id, string name) { }
    public Attachment(int id, Stream content) { }
}

public record Message(int Id, Attachment Attachment);
```

```text
AttachmentId | AttachmentName     -> Attachment(int, string)
AttachmentId | AttachmentContent  -> the Stream path is unavailable
```

`Stream` is not forbidden. It simply has no database-reading registration by default. A deliberate registration and parsing rule could make that path available.

## Generic wrappers

A normal mapped wrapper can declare everything it needs on the type.

```csharp
[method: AreReadable]
public readonly record struct DbValue<T>([NoName] T Value) : IDbReadable;
```

`IDbReadable` registers the wrapper when it is used. `[NoName]` lets its value use the same column name as the wrapper, and `[AreReadable]` makes `T` readable automatically.

```csharp
public record Actor(int Id, string Name);
public record AuditEntry(int Id, DbValue<Actor> Actor);

AuditEntry entry = GetAuditEntry.Query<AuditEntry>(cnn);
```

```text
Id | ActorId | ActorName -> AuditEntry(Id, DbValue(Actor))
```

Omit `[AreReadable]` when `T` must already be registered before the wrapper can use it.

[Positional wrappers](../customization/type-registration.md#register-a-positional-wrapper), [row-accumulating wrappers](../customization/multi-row.md), and [complete-result wrappers](../customization/result-parsers.md) use different advanced registration paths.

## Supply another parsing implementation

`AddOrSet` registers a specific `TypeParsingInfo` implementation.

```csharp
TypeParsingInfo.AddOrSet(typeof(LocalDate), new LocalDateTypeParsingInfo());
```

See [type registrations and defaults](../customization/type-registration.md) for custom scalar reading, initializers, generic precedence, removal, and parser invalidation. See [custom multi-row types](../customization/multi-row.md) for row accumulation.
