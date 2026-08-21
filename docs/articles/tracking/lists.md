# Tracking lists

`TrackingList<T>` tracks structural membership and order. It works with ordinary values and objects; generated editable items compose through a typed context.

## Materialize originals

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

Album[] originals = [new(1, "Blue"), new(2, "Green")];
TrackingList<IRuntimeTrackingItem<Album>> albums = originals.ToTrackingList();
```

## Added and removed rows

```csharp
albums.RemoveAt(0);

int removed = albums.RemovedCount;
int added = albums.AddedCount;
bool structural = albums.HasChanges;
```

`Removed` is real list-owned storage. `Added` is derived from active rows, using an item's read-only `IsNew` capability when present and compact fallback provenance otherwise. Moving a row preserves its provenance.

```csharp
albums.RestoreAt(0);
```

## Confirmation

Confirmation acknowledges successful external work; it is not persistence. Operations are per item and partial success is allowed.

```csharp
bool editConfirmed = albums.ConfirmEditAt(0);
bool allConfirmed = albums.ConfirmChanges();
```

Use `ConfirmAddedAt` for an active Added row and `ConfirmDeleteAt` for an item in `Removed`. A failed confirmation leaves list-owned state observable.

## Equality and source identity

Constructors and materialization overloads accept an `IEqualityComparer<T>` for logical list identity. Source-aware generated contexts separately map wrapper reference identity to source indexes, so equal objects in different source slots remain distinct.

## Binding support

Binding is a separate layer. `BindingTrackingList<T>` implements the BCL component-model contracts, forwards generated property notifications, and supports pending `AddNew`/`CancelNew` behavior.

```csharp
Rinku.Tracking.Binding.BindingTrackingList<IRuntimeTrackingItem<Album>> bindingAlbums =
    Rinku.Tracking.Binding.RuntimeBindingMaterializationExtensions.ToBindingList(originals);
IRuntimeTrackingItem<Album> pending = bindingAlbums.AddNew();
bindingAlbums.CancelNew(bindingAlbums.Count - 1);
```
