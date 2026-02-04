using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

public unsafe struct DefaultNoCache<T> : IParsingCache<T> {
    //private delegate*<DbDataReader, T> parser;
    private Func<DbDataReader, T> parser;
    public readonly CommandBehavior Behavior => default;
    public readonly bool IsValid => false;
    public void Init(DbDataReader reader, IDbCommand cmd)
        => parser = TypeParser<T>.GetParserFunc(reader.GetColumns());
    public readonly T Parse(DbDataReader reader) => parser(reader);
}
public static class DBCommandExtensions {
    extension(DbCommand cmd) {
        public int ExecuteQuery<T>(T cache, bool disposeCommand = true) where T : ICache {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteNonQuery();
                cache.UpdateCache(cmd);
                return res;
            }
            finally {
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
        public async Task<int> ExecuteQueryAsync<T>(T cache, bool disposeCommand = true, CancellationToken ct = default) where T : ICache {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                var res = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                cache.UpdateCache(cmd);
                return res;
            }
            finally {
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    await cmd.DisposeAsync().ConfigureAwait(false);
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }

        public void CacheSchema<TParser, T>(TParser cache) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = CommandBehavior.SchemaOnly;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                cache.Init(reader, cmd);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                cmd.Parameters.Clear();
                cmd.Dispose();
                if (wasClosed)
                    cnn.Close();
            }
        }

        public T? QuerySingle<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                cache.Init(reader, cmd);
                if (!reader.Read())
                    return default;
                return cache.Parse(reader);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
        public IEnumerable<T> QueryMultiple<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                cache.Init(reader, cmd);
                while (reader.Read())
                    yield return cache.Parse(reader);
                while (reader.NextResult()) { }
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }

        public async Task<T?> QuerySingleAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache.Init(reader, cmd);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return cache.Parse(reader);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
        public async IAsyncEnumerable<T> QueryMultipleAsync<TParser, T>(TParser cache, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache.Init(reader, cmd);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return cache.Parse(reader);
                while (await reader.NextResultAsync(ct).ConfigureAwait(false)) { }
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
        public async Task<T?> QuerySingleParseAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCacheAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                await cache.Init(reader, cmd).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return await cache.Parse(reader).ConfigureAwait(false);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
        public async IAsyncEnumerable<T> QueryMultipleParseAsync<TParser, T>(TParser cache, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) where TParser : IParsingCacheAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                await cache.Init(reader, cmd).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await cache.Parse(reader).ConfigureAwait(false);
                while (await reader.NextResultAsync(ct).ConfigureAwait(false)) { }
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
    }

#if !NET9_0_OR_GREATER
public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
{
    // On .NET 8+, the state machine for this is extremely lean
    foreach (var item in source)
    {
        yield return item;
        // This allows the loop to yield control if the consumer is awaiting
        await Task.Yield(); 
    }
}
#endif
    extension(IDbCommand cmd) {
        public int ExecuteQuery<T>(T cache, bool disposeCommand = true) where T : ICache {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteNonQuery();
                cache.UpdateCache(cmd);
                return res;
            }
            finally {
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
        public Task<int> ExecuteQueryAsync<T>(T cache, bool disposeCommand = true, CancellationToken ct = default) where T : ICache {
            if (cmd is DbCommand c)
                return c.ExecuteQueryAsync(cache, disposeCommand, ct);
            return Task.FromResult(cmd.ExecuteQuery(cache, disposeCommand));
        }

        public T? QuerySingle<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            if (cmd is DbCommand c)
                return c.QuerySingle<TParser, T>(cache, disposeCommand);
            return cmd.QuerySingleImpl<TParser, T>(cache, disposeCommand);
        }
        public IEnumerable<T> QueryMultiple<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            if (cmd is DbCommand c)
                return c.QueryMultiple<TParser, T>(cache, disposeCommand);
            return cmd.QueryMultipleImpl<TParser, T>(cache, disposeCommand);
        }

        private T? QuerySingleImpl<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache.Init(reader, cmd);
                if (!reader.Read())
                    return default;
                return cache.Parse(reader);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
        private IEnumerable<T> QueryMultipleImpl<TParser, T>(TParser cache, bool disposeCommand = true) where TParser : IParsingCache<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache.Init(reader, cmd);
                while (reader.Read())
                    yield return cache.Parse(reader);
                while (reader.NextResult()) { }
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }

        public Task<T?> QuerySingleAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCache<T> {
            if (cmd is DbCommand c)
                return c.QuerySingleAsync<TParser, T>(cache, disposeCommand, ct);
            return Task.FromResult(cmd.QuerySingleImpl<TParser, T>(cache, disposeCommand));
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCache<T> {
            if (cmd is DbCommand c)
                return c.QueryMultipleAsync<TParser, T>(cache, disposeCommand, ct);
            return cmd.QueryMultipleImpl<TParser, T>(cache, disposeCommand).ToAsyncEnumerable();
        }
        public Task<T?> QuerySingleParseAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCacheAsync<T> {
            if (cmd is DbCommand c)
                return c.QuerySingleParseAsync<TParser, T>(cache, disposeCommand, ct);
            return cmd.QuerySingleParseAsyncImpl<TParser, T>(cache, disposeCommand);
        }
        public IAsyncEnumerable<T> QueryMultipleParseAsync<TParser, T>(TParser cache, bool disposeCommand = true, CancellationToken ct = default) where TParser : IParsingCacheAsync<T> {
            if (cmd is DbCommand c)
                return c.QueryMultipleParseAsync<TParser, T>(cache, disposeCommand, ct);
            return cmd.QueryMultipleParseAsyncImpl<TParser, T>(cache, disposeCommand, ct);
        }

        private async Task<T?> QuerySingleParseAsyncImpl<TParser, T>(TParser cache, bool disposeCommand) where TParser : IParsingCacheAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                await cache.Init(reader, cmd).ConfigureAwait(false);
                if (!reader.Read())
                    return default;
                return await cache.Parse(reader).ConfigureAwait(false);
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
        private async IAsyncEnumerable<T> QueryMultipleParseAsyncImpl<TParser, T>(TParser cache, bool disposeCommand, [EnumeratorCancellation] CancellationToken __) where TParser : IParsingCacheAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                await cache.Init(reader, cmd).ConfigureAwait(false);
                while (reader.Read())
                    yield return await cache.Parse(reader).ConfigureAwait(false);
                while (reader.NextResult()) { }
            }
            finally {
                if (reader is not null) {
                    if (!reader.IsClosed) {
                        try { cmd.Cancel(); }
                        catch { }
                    }
                    reader.Dispose();
                }
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    cnn.Close();
            }
        }
    }
    extension(DbCommand cmd) {
        public int ExecuteQuery(bool disposeCommand = true)
            => cmd.ExecuteQuery<NoNeedToCache>(default, disposeCommand);
        public Task<int> ExecuteQueryAsync(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.ExecuteQueryAsync<NoNeedToCache>(default, disposeCommand, ct);

        public T? QuerySingle<T>(bool disposeCommand = true)
            => cmd.QuerySingle<DefaultNoCache<T>, T>(default, disposeCommand);
        public IEnumerable<T> QueryMultiple<T>(bool disposeCommand = true)
            => cmd.QueryMultiple<DefaultNoCache<T>, T>(default, disposeCommand);

        public Task<T?> QuerySingleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
    }
    extension(IDbCommand cmd) {
        public int ExecuteQuery(bool disposeCommand = true)
            => cmd.ExecuteQuery<NoNeedToCache>(default, disposeCommand);
        public Task<int> ExecuteQueryAsync(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.ExecuteQueryAsync<NoNeedToCache>(default, disposeCommand, ct);

        public T? QuerySingle<T>(bool disposeCommand = true)
            => cmd.QuerySingle<DefaultNoCache<T>, T>(default, disposeCommand);
        public IEnumerable<T> QueryMultiple<T>(bool disposeCommand = true)
            => cmd.QueryMultiple<DefaultNoCache<T>, T>(default, disposeCommand);

        public Task<T?> QuerySingleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
    }
}
/*
 
---

### 3. Asynchronous Mapping Alternatives (`ParseAsync`)

For specialized scenarios where the mapping process itself is I/O-bound, RinkuLib provides `ParseAsync` variants. These allow you to `await` logic inside the mapper for every row processed.

* **Methods**: `QuerySingleParseAsync<T>` and `QueryMultipleParseAsync<T>`.
* **Signature**: These require a **`Func<DbDataReader, Task<T>>`** instead of the standard **`Func<DbDataReader, T>`**.
* **Custom Mapping & Behavior:** You can bypass the automatic engine by providing a specific **`Func<DbDataReader, T>`**. This is a "plug-in" point where you define the construction logic. These overloads also allow you to specify a **`CommandBehavior`** (e.g., `SequentialAccess` or `SingleResult`) to fine-tune how the reader streams data.

```csharp
// Standard: Mapping Engine negotiates the parser automatically
var user = builder.QuerySingle<User>(cnn);

// Manual: You provide the func; Mapping Engine is bypassed
var name = builder.QuerySingle(cnn, reader => reader.GetString(0), CommandBehavior.SingleResult);

 */