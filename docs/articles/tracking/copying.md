# Copying

Tracking needs independent snapshots: an original to compare against, a state to revert to. `Copy<T>` makes one, with an IL-generated clone cached per type.

```csharp
var snapshot = original.Copy();
```

The default is a member-wise (shallow) clone. Attributes opt individual fields into deeper behavior.

## Field attributes

- `[Copy]` clones the field with `Copy<T>` (deep for that field).
- `[ShallowCollection]` clones the collection container, shares the elements.
- `[DeepCollection]` clones the container and clones each element.
- `[CopyUsingMethod("Name")]` calls a zero-argument instance method on the container and assigns its return value to the field.

Attributes are honored up the inheritance chain.

## A mixed snapshot

An invoice whose snapshot must own its line items but may share the immutable customer:

```csharp
public sealed class Invoice {
    public string Number = "";              // member-wise copy
    public Customer Customer = null!;       // shared reference, intentionally

    [DeepCollection]
    public List<InvoiceLine> Lines = [];    // new list, fresh line clones

    [Copy]
    public Address BillTo = null!;          // fresh Address
}

Invoice snapshot = invoice.Copy();
```

## Custom clone logic

Implement `ICopyable<T>` to take over the whole clone for a type. For direct collection clones without a containing object, `CollectionCopyExtensions` offers shallow (shared elements) and deep (cloned elements) copies.
