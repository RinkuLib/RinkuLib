# Transactions, timeouts, and cancellation

*Controlling the database context of a call.*

Every execution method, whether you call it on a `QueryCommand` or a builder, takes the same optional context arguments after the connection. On the command form they come after the optional parameter object, so name them.

## Transactions

Pass a transaction to enlist the command in it.

```csharp
using var trans = cnn.BeginTransaction();
cmd.Execute(cnn, transaction: trans);
trans.Commit();
```

## Timeouts

Override the default command timeout per call.

```csharp
var rows = cmd.Query<List<Track>>(cnn, timeout: 60);
```

## Cancellation (async)

Async methods take a `CancellationToken`.

```csharp
var tracks = await cmd.QueryAsync<List<Track>>(cnn, ct: token);
await foreach (var t in cmd.StreamQueryAsync<Track>(cnn, ct: token)) { }
```

> A [`QueryBuilderCommand`](builders.md) omits `connection`, `transaction`, and `timeout` (it manages its own `DbCommand`) but still accepts `ct` on async calls.
