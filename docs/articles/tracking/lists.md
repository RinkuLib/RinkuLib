# Tracking lists

`TrackingList<T>` tracks structural changes while keeping the active rows in their current order. It also understands editable tracking items when `T` exposes the tracking contracts.

## Materialize originals

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album[] originals = [new(1, "Blue"), new(2, "Green")];
TrackingList<IRuntimeDynamicTrackingItem<Album>> albums = originals.ToTrackingList();
```

## Added and removed rows

```csharp
albums.RemoveAt(0);

int removed = albums.RemovedCount;
int added = albums.AddedCount;
bool structural = albums.HasStructuralChanges;
```

`Removed` exposes removed original rows and `Added` exposes rows currently considered additions. Reordering with `Move` does not change the structural origin of a row.

A removed row can be restored before the structural change is accepted.

```csharp
albums.RestoreAt(0);
```

## Item and structural changes together

`HasChanges()` checks both list structure and active item edits.

```csharp
bool changed = albums.HasChanges();
```

After the application saves successfully, `CommitChanges()` accepts active item edits and list-owned structural changes. `CommitStructuralChanges()` accepts only list-owned structural state, while `CommitRemoved()` only clears the removed collection.

```csharp
albums.CommitChanges();
```

Call the commit only after the application persistence step succeeds. Tracking does not execute that persistence step itself.

## Equality and restored rows

Constructors and `ToTrackingList` overloads accept an `IEqualityComparer<T>`. The comparer is used when locating active or removed rows, which allows an application to use identity semantics other than reference equality.

## Binding support

`TrackingList<T>` implements `IBindingList`, `ITypedList`, and `ICancelAddNew`. Runtime materialization configures the list with generated property information and a new-item factory when the generated registration can create new originals.
