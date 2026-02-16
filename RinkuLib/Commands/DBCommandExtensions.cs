using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.Commands;
/// <summary>
/// The default implementation without a cache nor an allready set function
/// </summary>
public unsafe struct DefaultNoCache<T> : ISchemaParser<T> {
    private Func<DbDataReader, T> parser;
    /// <inheritdoc/>
    public readonly CommandBehavior Behavior => default;
    /// <inheritdoc/>
    public readonly bool IsInit => false;
    /// <inheritdoc/>
    public void Init(DbDataReader reader, IDbCommand cmd) {
        var schema = reader.GetColumns();
        parser = TypeParser<T>.GetParserFunc(ref schema);
    }
    /// <inheritdoc/>
    public readonly T Parse(DbDataReader reader) => parser(reader);
}
/// <summary>Extensions on DbCommand</summary>
public static class DBCommandExtensions {
    extension(DbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
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
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
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

        /// <summary>
        /// Executes the <see cref="DbCommand"/> to fecth the schema and send it to the cache.
        /// </summary>
        /// <param name="cache">A parser used as a cache since only init is called</param>
        public void CacheSchema<TParser, T>(TParser cache) where TParser : ISchemaParser<T> {
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

        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                parser.Init(reader, cmd);
                if (!reader.Read())
                    return default;
                return parser.Parse(reader);
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
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = cmd.ExecuteReader(behavior);
                parser.Init(reader, cmd);
                while (reader.Read())
                    yield return parser.Parse(reader);
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

        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async Task<T?> QuerySingleAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                parser.Init(reader, cmd);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return parser.Parse(reader);
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
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async IAsyncEnumerable<T> QueryMultipleAsync<TParser, T>(TParser parser, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                parser.Init(reader, cmd);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return parser.Parse(reader);
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
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row asynchronously to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async Task<T?> QuerySingleParseAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParserAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                await parser.Init(reader, cmd).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return default;
                return await parser.Parse(reader).ConfigureAwait(false);
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
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse each rows asynchronously to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async IAsyncEnumerable<T> QueryMultipleParseAsync<TParser, T>(TParser parser, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) where TParser : ISchemaParserAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                await parser.Init(reader, cmd).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await parser.Parse(reader).ConfigureAwait(false);
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
    internal static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source) {
        foreach (var item in source) {
            yield return item;
            await Task.Yield();
        }
    }
#endif
    extension(IDbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
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
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync<T>(T cache, bool disposeCommand = true, CancellationToken ct = default) where T : ICache {
            if (cmd is DbCommand c)
                return c.ExecuteQueryAsync(cache, disposeCommand, ct);
            return Task.FromResult(cmd.ExecuteQuery(cache, disposeCommand));
        }

        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            if (cmd is DbCommand c)
                return c.QuerySingle<TParser, T>(parser, disposeCommand);
            return cmd.QuerySingleImpl<TParser, T>(parser, disposeCommand);
        }
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            if (cmd is DbCommand c)
                return c.QueryMultiple<TParser, T>(parser, disposeCommand);
            return cmd.QueryMultipleImpl<TParser, T>(parser, disposeCommand);
        }

        private T? QuerySingleImpl<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                parser.Init(reader, cmd);
                if (!reader.Read())
                    return default;
                return parser.Parse(reader);
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
        private IEnumerable<T> QueryMultipleImpl<TParser, T>(TParser parser, bool disposeCommand = true) where TParser : ISchemaParser<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                parser.Init(reader, cmd);
                while (reader.Read())
                    yield return parser.Parse(reader);
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

        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParser<T> {
            if (cmd is DbCommand c)
                return c.QuerySingleAsync<TParser, T>(parser, disposeCommand, ct);
            return Task.FromResult(cmd.QuerySingleImpl<TParser, T>(parser, disposeCommand));
        }
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParser<T> {
            if (cmd is DbCommand c)
                return c.QueryMultipleAsync<TParser, T>(parser, disposeCommand, ct);
            return cmd.QueryMultipleImpl<TParser, T>(parser, disposeCommand).ToAsyncEnumerable();
        }
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse the first row asynchronously to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleParseAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParserAsync<T> {
            if (cmd is DbCommand c)
                return c.QuerySingleParseAsync<TParser, T>(parser, disposeCommand, ct);
            return cmd.QuerySingleParseAsyncImpl<TParser, T>(parser, disposeCommand);
        }
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse each rows asynchronously to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleParseAsync<TParser, T>(TParser parser, bool disposeCommand = true, CancellationToken ct = default) where TParser : ISchemaParserAsync<T> {
            if (cmd is DbCommand c)
                return c.QueryMultipleParseAsync<TParser, T>(parser, disposeCommand, ct);
            return cmd.QueryMultipleParseAsyncImpl<TParser, T>(parser, disposeCommand, ct);
        }

        private async Task<T?> QuerySingleParseAsyncImpl<TParser, T>(TParser parser, bool disposeCommand) where TParser : ISchemaParserAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior | CommandBehavior.SingleRow;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                await parser.Init(reader, cmd).ConfigureAwait(false);
                if (!reader.Read())
                    return default;
                return await parser.Parse(reader).ConfigureAwait(false);
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
        private async IAsyncEnumerable<T> QueryMultipleParseAsyncImpl<TParser, T>(TParser parser, bool disposeCommand, [EnumeratorCancellation] CancellationToken __) where TParser : ISchemaParserAsync<T> {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = parser.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                await parser.Init(reader, cmd).ConfigureAwait(false);
                while (reader.Read())
                    yield return await parser.Parse(reader).ConfigureAwait(false);
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
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public int ExecuteQuery(bool disposeCommand = true)
            => cmd.ExecuteQuery<NoNeedToCache>(default, disposeCommand);
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.ExecuteQueryAsync<NoNeedToCache>(default, disposeCommand, ct);

        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<T>(bool disposeCommand = true)
            => cmd.QuerySingle<DefaultNoCache<T>, T>(default, disposeCommand);
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<T>(bool disposeCommand = true)
            => cmd.QueryMultiple<DefaultNoCache<T>, T>(default, disposeCommand);

        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
    }
    extension(IDbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public int ExecuteQuery(bool disposeCommand = true)
            => cmd.ExecuteQuery<NoNeedToCache>(default, disposeCommand);
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<int> ExecuteQueryAsync(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.ExecuteQueryAsync<NoNeedToCache>(default, disposeCommand, ct);

        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<T>(bool disposeCommand = true)
            => cmd.QuerySingle<DefaultNoCache<T>, T>(default, disposeCommand);
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<T>(bool disposeCommand = true)
            => cmd.QueryMultiple<DefaultNoCache<T>, T>(default, disposeCommand);

        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<DefaultNoCache<T>, T>(default, disposeCommand, ct);
    }
    extension(DbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true)
            => cmd.QuerySingle<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand);
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true)
            => cmd.QueryMultiple<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand);

        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);

        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse asynchronously the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleParseAsync<T>(Func<DbDataReader, Task<T>> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleParseAsync<SchemaParserAsync<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse asynchronously each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleParseAsync<T>(Func<DbDataReader, Task<T>> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleParseAsync<SchemaParserAsync<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
    }
    extension(IDbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? QuerySingle<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true)
            => cmd.QuerySingle<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand);
        /// <summary>
        /// Executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public IEnumerable<T> QueryMultiple<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true)
            => cmd.QueryMultiple<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand);

        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleAsync<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleAsync<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleAsync<T>(Func<DbDataReader, T> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleAsync<SchemaParser<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);

        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse asynchronously the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QuerySingleParseAsync<T>(Func<DbDataReader, Task<T>> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QuerySingleParseAsync<SchemaParserAsync<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
        /// <summary>
        /// Asynchronously executes the <see cref="IDbCommand"/> and parse asynchronously each rows to return a collection of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parsingFunc">The function to use to parse the row</param>
        /// <param name="behavior">The base <see cref="CommandBehavior"/> for the <see cref="DbDataReader"/></param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public IAsyncEnumerable<T> QueryMultipleParseAsync<T>(Func<DbDataReader, Task<T>> parsingFunc, CommandBehavior behavior = default, bool disposeCommand = true, CancellationToken ct = default)
            => cmd.QueryMultipleParseAsync<SchemaParserAsync<T>, T>(new(parsingFunc, behavior), disposeCommand, ct);
    }
}