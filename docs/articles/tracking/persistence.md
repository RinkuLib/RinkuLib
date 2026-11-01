# Persistence

A committer is reusable. It consumes `ITrackingCommitSource<T>`: pending insert/update items, a span of removals and confirmation. `TrackingList<T>` supplies this contract, but another storage implementation can also supply it. Persistence does not require a particular list class.

The examples use the `Album`, `ITrackingItem<Album>`, and `albums` setup from the [tracking overview](index.md).

```csharp
static readonly QueryCommand InsertAlbum = new("INSERT INTO albums (Title) VALUES (@Title)");
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");
static readonly QueryCommand DeleteAlbum = new("DELETE FROM albums WHERE AlbumId = @Id");
static readonly QueryCommitter SaveAlbums = Committer.Create(InsertAlbum, UpdateAlbum, DeleteAlbum);

albums[0].Title = "Kind of Blue";
bool committed = SaveAlbums.Commit(cnn, albums);
```

A rejected validation or operation result returns `false` and leaves tracking changes pending.

## One independent item

A generated tracking item owns its accepted value, draft and new-item state. It can be saved without a containing list:

```csharp
ITrackingItem<Album> item = RuntimeTracking.Default<Album>().Create(original);
item.Set(nameof(Album.Title), "Kind of Blue");
bool committed = SaveAlbums.CommitItem(cnn, item);
```

`CommitItem` accepts an item implementing `ITrackingConfirmationSource` directly, without a collection adapter. A successful commit confirms the item after the database transaction succeeds. A failed commit leaves its draft pending.

Containing tracking lists subscribe to the item's `Confirmed` event, so a direct item save also updates their confirmation subscribers and bind-back targets. Disposing a list detaches its subscriptions without disposing the items. Resetting its baseline detaches the previous items. An item retained by a detail view therefore remains usable after its table list is disposed.

Items whose structural state belongs to a list are saved through that list with `Commit`.

## Generated ID

```csharp
static readonly QueryCommand InsertAlbumWithId = new("INSERT INTO albums (Title) VALUES (@Title); SELECT CAST(SCOPE_IDENTITY() AS int);");

// Assign the generated ID by member name.
static readonly ITrackingCommitter<DbConnection> SaveAlbumsWithNamedId = Committer.Create<int>(InsertAlbumWithId, nameof(Album.Id), UpdateAlbum, DeleteAlbum);

// Assign the generated ID with a delegate.
static readonly ITrackingCommitter<DbConnection> SaveAlbumsWithAssignedId = Committer.Create(InsertAlbumWithId, static (IAlbumEdit item, int id) => item.Id = id, UpdateAlbum, DeleteAlbum);

// Generated IDs are assigned before confirmation. They are not operation results.
```

## Command output

```csharp
public sealed record AlbumSaveResult(bool Accepted, int AlbumId);

// Load the stored procedure parameter metadata.
static readonly QueryCommand InsertAlbumWithOutput = QueryCommand.FromProc("InsertAlbum", cnn);

// Read @AlbumId and assign it to Album.Id.
static readonly ITrackingCommitter<DbConnection> SaveAlbumsWithOutput = Committer.Create<int>(InsertAlbumWithOutput, "@AlbumId", nameof(Album.Id), UpdateAlbum, DeleteAlbum);

// Execute reports affected rows. @AlbumId remains separate.

// Read both AlbumSaveResult and @AlbumId.
static readonly ITrackingCommitter<DbConnection> SaveAlbumsWithResult = Committer<AlbumSaveResult>.Create<int>(InsertAlbumWithOutput, "@AlbumId", nameof(Album.Id), UpdateAlbum, DeleteAlbum);
```

## Command factory

Generated command extensions can be used directly.

```csharp
static readonly DbCommandFactoryCommitter<ITrackingItem<Album>> DeleteAlbums = Committer.Create<ITrackingItem<Album>>(delete: static (item, connection) => connection.DeleteAlbum(albumId: item.Get<int>(nameof(Album.Id))));

bool committed = DeleteAlbums.Commit(cnn, albums);
```

## Staged parameters

An output type can be a typed parameter bag, including a value tuple. Operations populate these values without changing accepted tracking state. `Prepare` runs only after all operation results are accepted, while the transaction can still roll back. Output application runs after the database commits, followed by tracking confirmation.

```csharp
var save = Committer.Create<ITrackingItem<Album>, (int Id, Album? Original)>(InsertAlbumWithOutput,
    static command => (command.GetOutputValue<int>("@AlbumId"), null),
    static (item, parameters) => item.SetOriginal(parameters.Original!), UpdateAlbum, DeleteAlbum)
    .Prepare(static (item, parameters) => (parameters.Id, new Album(parameters.Id, item.Get<string>(nameof(Album.Title))!)));
```

A rejected result skips preparation. A preparation exception rolls back persistence. Each commit owns its staging values; failure and disposal clear them, so retries start fresh. `DbOutputState.Stage` is also available to custom operations, and `Reset` discards staged values without confirming tracking or completing the database transaction.

Command-factory output committers can stage update and delete values through `UpdateOutput` and `DeleteOutput`. They use the same preparation and application phases; the bag's shape and meaning belong to the caller.

## Changed-only query inputs

QueryCommand update committers use tracked changes for conditional template inputs. Unconditional inputs, such as an identity used by `WHERE`, remain supplied. SQL text and its behavior remain entirely authored in the template.

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE Albums SET Title = ?@Title, Artist = ?@Artist WHERE Id = @Id");
static readonly ITrackingCommitter<DbConnection> Albums = Committer.Create<ITrackingItem<Album>>(InsertAlbum, nameof(Album.Id), UpdateAlbum, DeleteAlbum);
```

Insert and delete can use command factories while update uses a QueryCommand. For direct use, `query.GetChangesCommand(item, connection)` creates the same changed-only command, and `query.StartBuilder().UseChanges(item)` exposes its builder.

A missing conditional input omits its SQL section. A changed null must be supplied as `DBNull.Value`; `[UseDbNull]` preserves that distinction for runtime tracking items. Parameter aliases and configured value conversions still apply. This does not filter changes by authorization: SQL permissions remain responsible for rejecting forbidden writes.

## Direct executor

```csharp
static readonly ExecutorCommitter<ITrackingItem<Album>, bool, int, bool> UpdateAlbums = Committer.Create<ITrackingItem<Album>, bool, int, bool>(update: static (item, connection, transaction) => UpdateAlbum.Execute(connection, item, transaction));

bool committed = UpdateAlbums.Commit(cnn, albums);
```

The committer interprets the returned value through its configured result callback. That callback decides acceptance and publishes any application metadata; tracking does not interpret persistence results.

[Transactions](../running-queries/execution-context.md) · [Tracking lists](lists.md)
