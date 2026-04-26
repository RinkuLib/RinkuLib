using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;
/// <summary>Extensions on DbCommand</summary>
public static class DBCommandExtensions {
    /// <summary>Return the parser of <typeparamref name="T"/> using the <paramref name="reader"/> current result schema</summary>
    public static ITypeParser<T> GetParser<T>(this DbDataReader reader) {
        var schema = reader.GetColumnsFast();
        return TypeParser<T>.GetTypeParser(ref schema);
    }
    extension(DbCommand cmd) {
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public int Execute(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteNonQuery();
                cache?.UpdateCache(cmd);
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
        public async Task<int> ExecuteAsync(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                var res = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                cache?.UpdateCache(cmd);
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
        /// Executes the <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T ExecuteScalar<T>(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteScalar();
                cache?.UpdateCache(cmd);
                return res.Parse<T>();
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
        /// Executes the <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async Task<T> ExecuteScalarAsync<T>(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                var res = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                cache?.UpdateCache(cmd);
                return res.Parse<T>();
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
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            if (cnn.State != ConnectionState.Open) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            var reader = cmd.ExecuteReader(behavior);
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            if (cnn.State != ConnectionState.Open) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
            }
            var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public MultiReader ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            var reader = cmd.ExecuteReader(behavior);
            if (command.NeedToCache(usageMap))
                command.UpdateCache(cmd);
            return new(usageMap, command, reader, cmd, disposeCommand, wasClosed);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public async Task<MultiReader> ExecuteMultiReaderAsync(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            if (wasClosed) {
                await cnn.OpenAsync(ct).ConfigureAwait(false);
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            if (command.NeedToCache(usageMap))
                command.UpdateCache(cmd);
            return new(usageMap, command, reader, cmd, disposeCommand, wasClosed);
        }
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse the rows to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? Query<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = CommandBehavior.SingleResult;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                using var reader = cmd.ExecuteReader(behavior);
                if (cache is not null) {
                    cache.UpdateCache(cmd, reader, ref parser);
                }
                else if (parser is null) {
                    var schema = reader.GetColumnsFast();
                    parser = TypeParser<T>.GetTypeParser(ref schema);
                }
                if (!reader.Read())
                    return parser.Default();
                if (parser is ILazyTypeParser<T> lazyParser) {
                    var res = lazyParser.ParseAndOwn(reader, cmd, wasClosed, disposeCommand);
                    disposeCommand = wasClosed = false;
                    return res;
                }
                return parser.Parse(reader);
            }
            finally {
                if (wasClosed)
                    cnn.Close();
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the rows to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async Task<T?> QueryAsync<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = CommandBehavior.SingleResult;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    cache.UpdateCache(cmd, reader, ref parser);
                }
                else if (parser is null) {
                    var schema = reader.GetColumnsFast();
                    parser = TypeParser<T>.GetTypeParser(ref schema);
                }
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return parser.Default();
                if (parser is ILazyTypeParser<T> lazyParser) {
                    var res = lazyParser.ParseAndOwn(reader, cmd, wasClosed, disposeCommand);
                    disposeCommand = wasClosed = false;
                    return res;
                }
                return await parser.ParseAsync(reader, ct);
            }
            finally {
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the rows to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async IAsyncEnumerable<T> StreamQueryAsync<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICache? cache = null, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = CommandBehavior.SingleResult;
                if (parser is IHasBehavior b)
                    behavior |= b.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                cache?.UpdateCache(cmd);
                if (parser is null) {
                    var schema = reader.GetColumnsFast();
                    parser = TypeParser<T>.GetTypeParser(ref schema);
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await parser.ParseAsync(reader, ct);
            }
            finally {
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
                if (wasClosed)
                    await cnn.CloseAsync().ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the rows to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public async IAsyncEnumerable<T> StreamQueryAsync<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = CommandBehavior.SingleResult;
                if (parser is IHasBehavior b)
                    behavior |= b.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (cache is not null) {
                    cache.UpdateCache(cmd, reader, ref parser);
                }
                else if (parser is null) {
                    var schema = reader.GetColumnsFast();
                    parser = TypeParser<T>.GetTypeParser(ref schema);
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    yield return await parser.ParseAsync(reader, ct);
            }
            finally {
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
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the nb of affected rows.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public int Execute(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteNonQuery();
                cache?.UpdateCache(cmd);
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
        public Task<int> ExecuteAsync(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteAsync(disposeCommand, cache, ct);
            return Task.FromResult(cmd.Execute(disposeCommand, cache));
        }
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T ExecuteScalar<T>(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                if (wasClosed)
                    cnn.Open();
                var res = cmd.ExecuteScalar();
                cache?.UpdateCache(cmd);
                return res.Parse<T>();
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
        /// Executes the <see cref="DbCommand"/> and return the scalar value.
        /// </summary>
        /// <param name="cache">A cache to be used after execution</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T> ExecuteScalarAsync<T>(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteScalarAsync<T>(disposeCommand, cache, ct);
            return Task.FromResult(cmd.ExecuteScalar<T>(disposeCommand, cache));
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            if (cnn.State != ConnectionState.Open) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
            }
            var r = cmd.ExecuteReader(behavior);
            var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteReaderAsync(behavior, cache, ct);
            return Task.FromResult(cmd.ExecuteReader(behavior, cache));
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public MultiReader ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            var r = cmd.ExecuteReader(behavior);
            var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
            if (command.NeedToCache(usageMap))
                command.UpdateCache(cmd);
            return new(usageMap, command, reader, cmd, disposeCommand, wasClosed);
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public Task<MultiReader> ExecuteMultiReaderAsync(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteMultiReaderAsync(command, usageMap, disposeCommand, behavior, ct);
            return Task.FromResult(cmd.ExecuteMultiReader(command, usageMap, disposeCommand, behavior));
        }
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        public T? Query<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null) {
            var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = CommandBehavior.SingleResult;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                    wasClosed = false;
                }
                var r = cmd.ExecuteReader(behavior);
                using var reader = r is DbDataReader rd ? rd : new WrappedBasicReader(r);
                if (cache is not null) {
                    cache.UpdateCache(cmd, reader, ref parser);
                }
                else if (parser is null) {
                    var schema = reader.GetColumnsFast();
                    parser = TypeParser<T>.GetTypeParser(ref schema);
                }
                if (!reader.Read())
                    return parser.Default();
                if (parser is ILazyTypeParser<T> lazyParser) {
                    var res = lazyParser.ParseAndOwn(reader, cmd, wasClosed, disposeCommand);
                    disposeCommand = wasClosed = false;
                    return res;
                }
                return parser.Parse(reader);
            }
            finally {
                if (wasClosed)
                    cnn.Close();
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
        /// </summary>
        /// <param name="parser">The item responsible to parse the rows</param>
        /// <param name="cache">A cache to be used with the reader</param>
        /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
        /// <param name="ct">The fowarded cancellation token</param>
        public Task<T?> QueryAsync<T>(bool disposeCommand = true, ITypeParser<T>? parser = null, ICacheUsingParser<T>? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QueryAsync(disposeCommand, parser, cache, ct);
            return Task.FromResult(cmd.Query(disposeCommand, parser, cache));
        }
    }
    /// <summary>Create the <see cref="DbCommand"/> associated with the <see cref="DbConnection"/> and set <see cref="DbTransaction"/> and timeout</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static DbCommand GetCommand(this DbConnection cnn, DbTransaction? transaction, int? timeout) {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        return cmd;
    }
    /// <summary>Create the <see cref="IDbCommand"/> associated with the <see cref="IDbConnection"/> and set <see cref="IDbTransaction"/> and timeout</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IDbCommand GetCommand(this IDbConnection cnn, IDbTransaction? transaction, int? timeout) {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        return cmd;
    }
}