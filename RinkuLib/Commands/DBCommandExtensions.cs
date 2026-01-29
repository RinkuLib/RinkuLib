using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.Commands;
public static class DBCommandExtensions {
    extension(DbCommand cmd) {
        public int Execute(bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                return cmd.ExecuteNonQuery();
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
        public async Task<int> ExecuteAsync(bool disposeCommand = true, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

        public T? QuerySingle<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                if (!reader.Read())
                    return default;
                return parser(reader);
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
        public IEnumerable<T> QueryMultiple<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                while (reader.Read())
                    yield return parser(reader);
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

        public async Task<T?> QuerySingleAsync<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return parser(reader);
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
        public async IAsyncEnumerable<T> QueryMultipleAsync<T>(Func<DbDataReader, T>? parser, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return parser(reader);
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
        public async Task<T?> QuerySingleParseAsync<T>(Func<DbDataReader, Task<T>>? parser, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return await parser(reader).ConfigureAwait(false);
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
        public async IAsyncEnumerable<T> QueryMultipleParseAsync<T>(Func<DbDataReader, Task<T>>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await parser(reader).ConfigureAwait(false);
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

        public async Task<T?> QuerySingleAsync<T>(Func<DbDataReader, T>? parser, IAsyncCache? cache, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    var p = await cache.UpdateCache(reader, cmd, parser).ConfigureAwait(false);
                    if (p is not null)
                        parser = p;
                }
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser))
                        throw new NotSupportedException();
                }
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return parser(reader);
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
        public async IAsyncEnumerable<T> QueryMultipleAsync<T>(Func<DbDataReader, T>? parser, IAsyncCache? cache, CommandBehavior behavior = default, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    var p = await cache.UpdateCache(reader, cmd, parser).ConfigureAwait(false);
                    if (p is not null)
                        parser = p;
                }
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser))
                        throw new NotSupportedException();
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return parser(reader);
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
        public async Task<T?> QuerySingleParseAsync<T>(Func<DbDataReader, Task<T>>? parser, IAsyncCache? cache, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    var p = await cache.UpdateCache(reader, cmd, parser).ConfigureAwait(false);
                    if (p is not null)
                        parser = p;
                }
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return await parser(reader).ConfigureAwait(false);
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
        public async IAsyncEnumerable<T> QueryMultipleParseAsync<T>(Func<DbDataReader, Task<T>>? parser, IAsyncCache? cache, CommandBehavior behavior = default, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    var p = await cache.UpdateCache(reader, cmd, parser).ConfigureAwait(false);
                    if (p is not null)
                        parser = p;
                }
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await parser(reader).ConfigureAwait(false);
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
    extension(IDbCommand cmd) {
        public int ExecuteQuery(bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                return cmd.ExecuteNonQuery();
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
        public Task<int> ExecuteQueryAsync(bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteAsync(disposeCommand, ct);
            return Task.FromResult(cmd.ExecuteQuery(disposeCommand));
        }

        public T? QuerySingle<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            if (cmd is DbCommand c)
                return c.QuerySingle(parser, cache, behavior, disposeCommand);
            return cmd.QuerySingleImpl(parser, cache, behavior, disposeCommand);
        }
        public IEnumerable<T> QueryMultiple<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            if (cmd is DbCommand c)
                return c.QueryMultiple(parser, cache, behavior, disposeCommand);
            return cmd.QueryMultipleImpl(parser, cache, behavior, disposeCommand);
        }

        private T? QuerySingleImpl<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                if (!reader.Read())
                    return default;
                return parser(reader);
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
        private IEnumerable<T> QueryMultipleImpl<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null && !TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out parser!))
                    throw new NotSupportedException();
                while (reader.Read())
                    yield return parser(reader);
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



        public Task<T?> QuerySingleAsync<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QuerySingleAsync(parser, cache, behavior, disposeCommand, ct);
            return Task.FromResult(cmd.QuerySingleImpl(parser, cache, behavior, disposeCommand));
        }
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(Func<DbDataReader, T>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QueryMultipleAsync(parser, cache, behavior, disposeCommand, ct);
            return cmd.QueryMultipleImpl(parser, cache, behavior, disposeCommand).ToAsyncEnumerable();
        }
        public Task<T?> QuerySingleParseAsync<T>(Func<DbDataReader, Task<T>>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QuerySingleParseAsync(parser, cache, behavior, disposeCommand, ct);
            return cmd.QuerySingleParseAsyncImpl(parser, cache, behavior, disposeCommand);
        }
        public IAsyncEnumerable<T> QueryMultipleParseAsync<T>(Func<DbDataReader, Task<T>>? parser = null, ICache? cache = null, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QueryMultipleParseAsync(parser, cache, behavior, disposeCommand, ct);
            return cmd.QueryMultipleParseAsyncImpl(parser, cache, behavior, disposeCommand, ct);
        }

        private async Task<T?> QuerySingleParseAsyncImpl<T>(Func<DbDataReader, Task<T>>? parser, ICache? cache, CommandBehavior behavior, bool disposeCommand) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                if (!reader.Read())
                    return default;
                return await parser(reader).ConfigureAwait(false);
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
        private async IAsyncEnumerable<T> QueryMultipleParseAsyncImpl<T>(Func<DbDataReader, Task<T>>? parser, ICache? cache, CommandBehavior behavior, bool disposeCommand, [EnumeratorCancellation] CancellationToken __) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                behavior |= CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                cache?.UpdateCache(reader, cmd, ref parser);
                if (parser is null) {
                    if (!TypeParser<T>.TryGetParser(reader.GetColumns(), out _, out var par))
                        throw new NotSupportedException();
                    parser = r => Task.FromResult(par(r));
                }
                while (reader.Read())
                    yield return await parser(reader).ConfigureAwait(false);
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
}
