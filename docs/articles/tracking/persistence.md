# Persistence

Tracking reports changes. Application code decides how those changes are stored.

Confirm a tracked operation only after the matching persistence operation succeeds.

## Save one edited item

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

if (edit.HasChanges())
{
    UpdateAlbum.Execute(cnn, edit);
    edit.ConfirmEdit();
}
```

Generated tracking items can be used as query values when their projected members match the command parameters.

## Cancel instead of saving

```csharp
edit.CancelEdit();
```

Cancel discards the edit snapshot. It does not call the database.

## Save an added item

```csharp
IRuntimeTrackingItem<Album> added = albums.Added[0];

InsertAlbum.Execute(cnn, added);
albums.Confirm(added);
```

`Confirm()` confirms the active operation. An item that is still structurally new is confirmed as an addition.

## Save a removed item

```csharp
IRuntimeTrackingItem<Album> removed = albums.Removed[0];

DeleteAlbum.Execute(cnn, removed);
albums.ConfirmDelete(removed);
```

The removed item stays available until deletion is confirmed.

## Save an edited existing item

```csharp
for (int i = 0; i < albums.Count; i++)
{
    IRuntimeTrackingItem<Album> item = albums[i];

    if (albums.IsAddedAt(i) || !item.HasChanges())
        continue;

    UpdateAlbum.Execute(cnn, item);
    albums.ConfirmEditAt(i);
}
```

The list confirms the edit through its context. The item edit state is accepted when that confirmation succeeds.

## Use a transaction

Persist every operation first and confirm tracking state after the transaction commits.

```csharp
using DbTransaction tx = cnn.BeginTransaction();

foreach (IRuntimeTrackingItem<Album> item in albums.Added)
    InsertAlbum.Execute(cnn, item, transaction: tx);

foreach (IRuntimeTrackingItem<Album> item in albums.Removed)
    DeleteAlbum.Execute(cnn, item, transaction: tx);

for (int i = 0; i < albums.Count; i++)
{
    IRuntimeTrackingItem<Album> item = albums[i];

    if (albums.IsAddedAt(i) || !item.HasChanges())
        continue;

    UpdateAlbum.Execute(cnn, item, transaction: tx);
}

tx.Commit();
albums.ConfirmChanges();
```

This keeps tracked state unchanged when the transaction fails before commit.

See [execution context](../running-queries/execution-context.md) for transaction handling in queries.

## Use application persistence code

Tracking does not require SQL persistence. The same confirmation rule applies to another store or service.

```csharp
await repository.UpdateAsync(edit, cancellationToken);
edit.ConfirmEdit();
```

The persistence mechanism and the confirmation point belong to the application.
