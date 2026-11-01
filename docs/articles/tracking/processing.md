# Persistence processing and tracking confirmation

A committer runs preprocessing, database operations, and postprocessing. It stores named returned values in each tracking item's pending parameters. It does not construct domain objects or replace accepted originals.

```csharp
public static RuntimeTrackingOptions<User> Tracking { get; } = RuntimeTracking.CreateOptions<User, ITrackingItem<User>>()
    .ConstructOnInsert();

var insert = CommitOperation.Command<ITrackingItem<User>, ValidationErrors, long?>(DbCommands.InsertUser, ReadErrors, CommandOutput.Named<long?>(nameof(User.Id)))
    .AfterEach(static (item, result) => item.ApplyValidation(result.Result));
var update = CommitOperation.Changes<ITrackingItem<User>, ValidationErrors>(Queries.SaveUser, ReadErrors)
    .AfterEach(static (item, errors) => item.ApplyValidation(errors));
var delete = CommitOperation.Command<ITrackingItem<User>, int>(DbCommands.DeleteUser, static command => command.ExecuteNonQuery());
var committer = Committer.For<ITrackingItem<User>>()
    .BeforeEach(Validate)
    .Insert(insert)
    .Update(update)
    .Delete(delete);
```

Insert, update, and delete can return different result types. `Upsert` assigns one operation to both insertion and update. General `BeforeEach`, `AfterEach`, `Then`, and `Select` processing remains available.

## Named values

`CommandOutput.Named<T>(destinationName, sourceName)` selects a typed factory output. One, two, and three output overloads are supported. When all outputs are selected, omitted source names use factory declaration order. Selecting a subset requires explicit source names. Destination names must be unique.

For a scalar processing result, `.Named(name)` stores that result as a pending value. Unnamed ordinary results are only used by processing callbacks. Command outputs must be named before attaching their operation to a committer.

Pending parameters are allocated only when a named value is supplied. They are separate from edits and validation metadata. Rejection or an exception during persistence rolls back the database and discards pending parameters, while edits and validation messages remain. A retry starts with no pending values from the previous attempt.

## Confirmation ownership

`ConstructOnInsert()` constructs the first accepted original from pending parameters and current edited values. Updates use the existing snapshot confirmation: editable values are applied to the same original instance.

Use `ConstructOnConfirm()` explicitly when updates should instead replace the original. Pending values take precedence. Constructor selection happens once during configuration; supply an explicit constructor when several exist.

Readonly original members require a pending value on insertion, or an existing original on update. Missing required values produce a typed binding exception.

`ConfirmUsing(item => ..., TrackingConfirmation.Insert)` supplies custom insertion logic; use `TrackingConfirmation.Update` for custom update logic, or omit the operation to apply it to both. It returns the accepted original, which tracking stores and confirms. Without a construction callback, existing mutable-original snapshot confirmation remains available.

The committer completes all database operations and checks their results before committing the transaction. Only after persistence succeeds does it call tracking confirmation. A confirmation exception is a configuration/programming failure after persistence; it cannot roll back an already committed database transaction. Confirmation clears the item's consumed pending parameters.
