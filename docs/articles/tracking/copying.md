# Copying tracked values

`Copy<T>` creates the snapshots used by editable items. The generated clone is cached per type.

```csharp
var snapshot = original.Copy();
```

The default is a shallow member-wise clone. Attributes select deeper behavior for individual fields.

## Copy one field deeply

`[Copy]` clones the field through its own `Copy<T>` behavior.

```csharp
public sealed class Invoice {
    public string Number = "";

    [Copy]
    public Address BillTo = new();
}
```

```csharp
Invoice snapshot = invoice.Copy() ?? throw new InvalidOperationException("Copy failed.");

snapshot.BillTo.Street = "Changed";
// invoice.BillTo remains unchanged.
```

## Copy a collection container

`[ShallowCollection]` creates another collection and shares its elements.

```csharp
public sealed class Playlist {
    [ShallowCollection]
    public List<Track> Tracks = [];
}
```

```text
snapshot.Tracks                 -> different list
snapshot.Tracks[0]              -> same Track reference
```

## Copy a collection and its elements

`[DeepCollection]` clones both the collection and every element.

```csharp
public sealed class Invoice {
    [DeepCollection]
    public List<InvoiceLine> Lines = [];
}
```

```text
snapshot.Lines                  -> different list
snapshot.Lines[0]               -> cloned InvoiceLine
```

## Call a copy method

`[CopyUsingMethod]` calls a zero-argument instance method and assigns its return value.

```csharp
public sealed class Report {
    [CopyUsingMethod(nameof(CloneSettings))]
    public ReportSettings Settings = new();

    ReportSettings CloneSettings() => new(Settings);
}
```

Copy attributes are honored through the inheritance chain.

## Mix shallow and deep fields

```csharp
public sealed class Invoice {
    public string Number = "";
    public Customer Customer = new();

    [DeepCollection]
    public List<InvoiceLine> Lines = [];

    [Copy]
    public Address BillTo = new();
}
```

```text
Number    -> copied value
Customer  -> shared reference
Lines     -> new list with cloned elements
BillTo    -> cloned object
```

## Replace copying for one type

Implement `ICopyable<T>` when the type should own its complete clone operation.

```csharp
public sealed class Report : ICopyable<Report> {
    public string Title { get; init; } = "";

    public Report Copy() => new() { Title = Title };
}

Report snapshot = report.Copy();
```

For collections copied outside a containing object, `CollectionCopyExtensions` exposes shallow and deep operations directly.

```csharp
List<Report> shallow = reports.ShallowCopy();
List<Report> deep = reports.DeepCopy();
```

## Configure an external type

Register a field plan during application setup when attributes cannot be added to the type.

```csharp
public sealed class ExternalInvoice {
    public Address Address = new();
}

FieldInfo address = typeof(ExternalInvoice).GetField(nameof(ExternalInvoice.Address)) ?? throw new InvalidOperationException("Address was not found.");

Copier<ExternalInvoice>.RegisterFieldPlan(address, new CopyAttribute());
```

```csharp
ExternalInvoice snapshot = source.Copy() ?? throw new InvalidOperationException("Copy failed.");
```

`ICopyFieldPlan` is the registration contract. `CopyAttribute`, `ShallowCollectionAttribute`, `DeepCollectionAttribute`, and `CopyUsingMethodAttribute` are its built-in implementations.
