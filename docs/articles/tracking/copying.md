# Copying and cloning

*Independent snapshots via fast, IL-generated clones.*

[Tracking](overview.md) often needs an independent copy of an object, an original to compare against, or a snapshot to revert to. `CopyExtensions.Copy<T>` makes one, choosing an efficient strategy for the type's structure.

```csharp
var snapshot = original.Copy();   // a logically independent clone
```

## Customizing how a type copies

- Implement **`ICopyable<T>`** to provide your own clone logic for a type.
- Decorate fields with attributes derived from **`CopyFieldAttribute`** (which emits IL for that field) to control field-level behavior.
  - **`[Copy]`** clones the field with `CopyExtensions.Copy<T>`.
  - **`[ShallowCollection]`** clones the collection container but shares element references.
  - **`[DeepCollection]`** clones the container and recursively clones each element.
  - **`[CopyUsingMethod("MethodName")]`** clones the field by calling an instance method that returns the value to assign.

## Collection helpers

`CollectionCopyExtensions` offers direct collection clones. A shallow copy clones the container and shares elements. A deep copy clones the container and `Copy`s each element.

## Mixed shallow and deep snapshot

The default `Copy<T>` is a member-wise (shallow) clone. The field attributes opt individual fields into deeper behavior. Consider an invoice whose snapshot must be independent on the line items but may share the immutable customer reference.

```csharp
public sealed class Invoice {
    public string Number = "";                  // value-ish, member-wise copy is fine
    public Customer Customer = null!;           // shared reference, leave shallow

    [DeepCollection]                            // clone the list AND each line
    public List<InvoiceLine> Lines = [];

    [Copy]                                       // clone this reference field deeply
    public Address BillTo = null!;
}

Invoice snapshot = invoice.Copy();
```

The result.

- `snapshot.Number` is copied (independent).
- `snapshot.Customer` is the **same instance** as `invoice.Customer` (intentionally shared).
- `snapshot.Lines` is a new `List<InvoiceLine>` whose elements are fresh clones (`[DeepCollection]`). Use `[ShallowCollection]` instead for a new list that shares the original line objects.
- `snapshot.BillTo` is a fresh `Address` (`[Copy]`).

For a field that needs bespoke logic, `[CopyUsingMethod("BuildSnapshot")]` calls a zero-arg instance method on the container and assigns its return value to the field. Field attributes are honored up the inheritance chain, and the clone strategy is IL-generated and cached per type, so repeated copies are cheap.
