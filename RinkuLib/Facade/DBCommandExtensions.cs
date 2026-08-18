using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Rinku.Mapping;
using Rinku.Querying;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku;
/// <summary>Runs and reads a database command that the caller created.</summary>
public static class DBCommandExtensions {
    private static DbParameter GetNamedParameter(DbCommand command, string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var parameters = command.Parameters;
        if (!parameters.Contains(name))
            throw new KeyNotFoundException($"The command has no parameter named '{name}'.");
        return parameters[name];
    }
    private static IDbDataParameter GetNamedParameter(IDbCommand command, string name) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var parameters = command.Parameters;
        if (!parameters.Contains(name) || parameters[name] is not IDbDataParameter parameter)
            throw new KeyNotFoundException($"The command has no parameter named '{name}'.");
        return parameter;
    }

    extension(DbCommand cmd) {
        /// <summary>Finds the parameter that receives the stored procedure's return value.</summary>
        /// <exception cref="KeyNotFoundException">The command has no return-value parameter.</exception>
        public DbParameter GetReturnParameter() {
            var parameters = cmd.Parameters;
            for (int i = 0; i < parameters.Count; i++)
                if (parameters[i].Direction == ParameterDirection.ReturnValue)
                    return parameters[i];
            throw new KeyNotFoundException("The command has no return-value parameter.");
        }
        /// <summary>Reads and converts the stored procedure return value from its parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no return-value parameter.</exception>
        public T? GetReturnValue<T>() => cmd.GetReturnParameter().Value.Parse<T>();

        /// <summary>Finds a named output or input-output parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no parameter with that name.</exception>
        /// <exception cref="InvalidOperationException">The named parameter is not an output parameter.</exception>
        public DbParameter GetOutputParameter(string name) {
            var parameter = GetNamedParameter(cmd, name);
            if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
                throw new InvalidOperationException($"The parameter '{name}' is not an output parameter.");
            return parameter;
        }
        /// <summary>Reads and converts a named output or input-output parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no parameter with that name.</exception>
        /// <exception cref="InvalidOperationException">The named parameter is not an output parameter.</exception>
        public T? GetOutputValue<T>(string name) => cmd.GetOutputParameter(name).Value.Parse<T>();

        /// <summary>
        /// Executes the <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public int Execute(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// Executes the <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async Task<int> ExecuteAsync(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// Executes the <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public T? ExecuteScalar<T>(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// Executes the <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async Task<T?> ExecuteScalarAsync<T>(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader reader;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = cmd.ExecuteReader(behavior);
            }
            catch {
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                throw;
            }
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, ICache? cache = null, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader reader;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
            }
            catch {
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    await cnn.CloseAsync().ConfigureAwait(false);
                throw;
            }
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public MultiReader ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = cmd.ExecuteReader(behavior);
                if (command.NeedToCache(usageMap))
                    command.UpdateCache(cmd);
                return new(usageMap, command, reader, cmd, disposeCommand, false);
            }
            catch {
                try {
                    reader?.Dispose();
                }
                finally {
                    try {
                        if (disposeCommand) {
                            cmd.Parameters.Clear();
                            cmd.Dispose();
                        }
                    }
                    finally {
                        if (wasClosed && cnn.State != ConnectionState.Closed)
                            cnn.Close();
                    }
                }
                throw;
            }
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public async Task<MultiReader> ExecuteMultiReaderAsync(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default, CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                if (command.NeedToCache(usageMap))
                    command.UpdateCache(cmd);
                return new(usageMap, command, reader, cmd, disposeCommand, false);
            }
            catch {
                try {
                    if (reader is not null)
                        await reader.DisposeAsync().ConfigureAwait(false);
                }
                finally {
                    try {
                        if (disposeCommand) {
                            cmd.Parameters.Clear();
                            await cmd.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally {
                        if (wasClosed && cnn.State != ConnectionState.Closed)
                            await cnn.CloseAsync().ConfigureAwait(false);
                    }
                }
                throw;
            }
        }
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public T Query<T>(ICacheGivingParser<T> cache, bool disposeCommand = true) {
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStart<T>(cmd, cache, disposeCommand, out var cold))
                    return cold;
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = cmd.ExecuteReader(behavior);
                wasClosed = false;
                var parser = cache.UpdateCache(cmd, reader);
                T result;
                if (!reader.Read())
                    result = parser.Default();
                else if (parser is ISimpleParser<T> simple)
                    result = simple.RowParser(reader);
                else
                    result = parser.Parse(reader).Result;
                ResultSetDrainer.Drain(reader);
                return result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async Task<T> QueryAsync<T>(ICacheGivingParser<T> cache, bool disposeCommand = true, CancellationToken ct = default) {
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStartAsync<T>(cmd, cache, disposeCommand, ct, out var cold))
                    return await cold.ConfigureAwait(false);
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                wasClosed = false;
                var parser = await cache.UpdateCacheAsync(cmd, reader, ct).ConfigureAwait(false);
                T result;
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    result = parser.Default();
                else if (parser is ISimpleParser<T> simple)
                    result = simple.RowParser(reader);
                else
                    result = (await parser.ParseAsync(reader, ct).ConfigureAwait(false)).Result;
                await ResultSetDrainer.DrainAsync(reader, ct).ConfigureAwait(false);
                return result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
            }
        }
        public object? Query(Type type, ICacheGivingParser cache, bool disposeCommand = true) {
            ArgumentNullException.ThrowIfNull(type);
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStart(type, cmd, cache, disposeCommand, out var cold))
                    return cold!;
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) { cnn.Open(); behavior |= CommandBehavior.CloseConnection; }
                reader = cmd.ExecuteReader(behavior);
                wasClosed = false;
                var parser = cache.UpdateCache(cmd, reader);
                var result = !reader.Read() ? parser.DefaultObject() : parser.ParseObject(reader).Result;
                ResultSetDrainer.Drain(reader);
                return result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed) cnn.Close();
                if (disposeCommand) { cmd.Parameters.Clear(); cmd.Dispose(); }
            }
        }
        public async Task<object?> QueryAsync(Type type, ICacheGivingParser cache, bool disposeCommand = true, CancellationToken ct = default) {
            ArgumentNullException.ThrowIfNull(type);
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStartAsync(type, cmd, cache, disposeCommand, ct, out var cold))
                    return await cold!.ConfigureAwait(false);
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) { await cnn.OpenAsync(ct).ConfigureAwait(false); behavior |= CommandBehavior.CloseConnection; }
                reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                wasClosed = false;
                var parser = await cache.UpdateCacheAsync(cmd, reader, ct).ConfigureAwait(false);
                var result = !await reader.ReadAsync(ct).ConfigureAwait(false)
                    ? parser.DefaultObject()
                    : (await parser.ParseObjectAsync(reader, ct).ConfigureAwait(false)).Result;
                await ResultSetDrainer.DrainAsync(reader, ct).ConfigureAwait(false);
                return result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed) cnn.Close();
                if (disposeCommand) { cmd.Parameters.Clear(); await cmd.DisposeAsync().ConfigureAwait(false); }
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and streams its rows as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parser">The parser that reads the rows.</param>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async IAsyncEnumerable<T> StreamQueryAsync<T>(ITypeParser<T> parser, ICache? cache = null, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                CommandBehavior behavior = parser.Behavior & ~CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                }
                using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                wasClosed = false;
                cache?.UpdateCache(cmd);
                if (parser is ISimpleParser<T> simple) {
                    var rowParser = simple.RowParser;
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        yield return rowParser(reader);
                }
                else if (await reader.ReadAsync(ct).ConfigureAwait(false)) {
                    bool canContinue;
                    do {
                        (canContinue, var item) = await parser.ParseAsync(reader, ct).ConfigureAwait(false);
                        yield return item;
                    } while (canContinue);
                }
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
        /// Asynchronously executes the <see cref="DbCommand"/> and streams its rows as <typeparamref name="T"/>.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public async IAsyncEnumerable<T> StreamQueryAsync<T>(ICacheGivingParser<T> cache, bool disposeCommand = true, [EnumeratorCancellation] CancellationToken ct = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            try {
                var behavior = cache.Behavior & ~CommandBehavior.SingleRow;
                if (wasClosed) {
                    await cnn.OpenAsync(ct).ConfigureAwait(false);
                    behavior |= CommandBehavior.CloseConnection;
                }
                using var reader = await cmd.ExecuteReaderAsync(behavior, ct).ConfigureAwait(false);
                wasClosed = false;
                var parser = await cache.UpdateCacheAsync(cmd, reader, ct).ConfigureAwait(false);
                if (parser is ISimpleParser<T> simple) {
                    var rowParser = simple.RowParser;
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        yield return rowParser(reader);
                }
                else if (await reader.ReadAsync(ct).ConfigureAwait(false)) {
                    bool canContinue;
                    do {
                        (canContinue, var item) = await parser.ParseAsync(reader, ct).ConfigureAwait(false);
                        yield return item;
                    } while (canContinue);
                }
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
        /// <summary>Finds the parameter that receives the stored procedure's return value.</summary>
        /// <exception cref="KeyNotFoundException">The command has no return-value parameter.</exception>
        public IDbDataParameter GetReturnParameter() {
            var parameters = cmd.Parameters;
            for (int i = 0; i < parameters.Count; i++)
                if (parameters[i] is IDbDataParameter parameter && parameter.Direction == ParameterDirection.ReturnValue)
                    return parameter;
            throw new KeyNotFoundException("The command has no return-value parameter.");
        }
        /// <summary>Reads and converts the stored procedure return value from its parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no return-value parameter.</exception>
        public T? GetReturnValue<T>() => cmd.GetReturnParameter().Value.Parse<T>();

        /// <summary>Finds a named output or input-output parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no parameter with that name.</exception>
        /// <exception cref="InvalidOperationException">The named parameter is not an output parameter.</exception>
        public IDbDataParameter GetOutputParameter(string name) {
            var parameter = GetNamedParameter(cmd, name);
            if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
                throw new InvalidOperationException($"The parameter '{name}' is not an output parameter.");
            return parameter;
        }
        /// <summary>Reads and converts a named output or input-output parameter.</summary>
        /// <exception cref="KeyNotFoundException">The command has no parameter with that name.</exception>
        /// <exception cref="InvalidOperationException">The named parameter is not an output parameter.</exception>
        public T? GetOutputValue<T>(string name) => cmd.GetOutputParameter(name).Value.Parse<T>();

        /// <summary>
        /// Executes the <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public int Execute(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// Executes the <see cref="DbCommand"/> and returns the number of affected rows.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<int> ExecuteAsync(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteAsync(disposeCommand, cache, ct);
            return Task.FromResult(cmd.Execute(disposeCommand, cache));
        }
        /// <summary>
        /// Executes the <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public T? ExecuteScalar<T>(bool disposeCommand, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
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
        /// Executes the <see cref="DbCommand"/> and returns the scalar value.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings from the executed command.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T?> ExecuteScalarAsync<T>(bool disposeCommand, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteScalarAsync<T>(disposeCommand, cache, ct);
            return Task.FromResult(cmd.ExecuteScalar<T>(disposeCommand, cache));
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        public DbDataReader ExecuteReader(CommandBehavior behavior = default, ICache? cache = null) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader reader;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = WrappedBasicReader.Wrap(r);
            }
            catch {
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                throw;
            }
            cache?.UpdateCache(cmd);
            return reader;
        }
        /// <summary>
        /// Executes the reader of the <see cref="DbCommand"/>.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="behavior">The default behavior to use for the reader</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<DbDataReader> ExecuteReaderAsync(CommandBehavior behavior = default, ICache? cache = null, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.ExecuteReaderAsync(behavior, cache, ct);
            return Task.FromResult(cmd.ExecuteReader(behavior, cache));
        }
        /// <summary>
        /// Executes the <see cref="MultiReader"/> of the <see cref="DbCommand"/>.
        /// </summary>
        public MultiReader ExecuteMultiReader(QueryCommand command, bool[] usageMap, bool disposeCommand, CommandBehavior behavior = default) {
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader reader;
            try {
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = WrappedBasicReader.Wrap(r);
            }
            catch {
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                throw;
            }
            if (command.NeedToCache(usageMap))
                command.UpdateCache(cmd);
            return new(usageMap, command, reader, cmd, disposeCommand, false);
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
        /// Executes the <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        public T Query<T>(ICacheGivingParser<T> cache, bool disposeCommand = true) {
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStart<T>(cmd, cache, disposeCommand, out var cold))
                    return cold;
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) {
                    cnn.Open();
                    behavior |= CommandBehavior.CloseConnection;
                }
                var r = cmd.ExecuteReader(behavior);
                reader = WrappedBasicReader.Wrap(r);
                wasClosed = false;
                var parser = cache.UpdateCache(cmd, reader);
                if (!reader.Read())
                    return parser.Default();
                if (parser is ISimpleParser<T> simple)
                    return simple.RowParser(reader);
                return parser.Parse(reader).Result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed)
                    cnn.Close();
                if (disposeCommand) {
                    cmd.Parameters.Clear();
                    cmd.Dispose();
                }
            }
        }
        /// <summary>
        /// Asynchronously executes the <see cref="DbCommand"/> and reads the result as <typeparamref name="T"/>. The requested type controls the result.
        /// </summary>
        /// <param name="cache">A cache that records parameter settings and selects a parser for the reader.</param>
        /// <param name="disposeCommand">Whether to dispose the command after execution.</param>
        /// <param name="ct">The token that can stop the operation.</param>
        public Task<T> QueryAsync<T>(ICacheGivingParser<T> cache, bool disposeCommand = true, CancellationToken ct = default) {
            if (cmd is DbCommand c)
                return c.QueryAsync(cache, disposeCommand, ct);
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStartAsync<T>(cmd, cache, disposeCommand, ct, out var cold))
                    return cold;
            return Task.FromResult(cmd.Query(cache, disposeCommand));
        }
        public object? Query(Type type, ICacheGivingParser cache, bool disposeCommand = true) {
            ArgumentNullException.ThrowIfNull(type);
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStart(type, cmd, cache, disposeCommand, out var cold))
                    return cold!;
            var cnn = cmd.Connection ?? throw new RinkuNoConnectionException();
            var wasClosed = cnn.State != ConnectionState.Open;
            DbDataReader? reader = null;
            try {
                var behavior = cache.Behavior;
                if (wasClosed) { cnn.Open(); behavior |= CommandBehavior.CloseConnection; }
                reader = WrappedBasicReader.Wrap(cmd.ExecuteReader(behavior));
                wasClosed = false;
                var parser = cache.UpdateCache(cmd, reader);
                return !reader.Read() ? parser.DefaultObject() : parser.ParseObject(reader).Result;
            }
            finally {
                reader?.Dispose();
                if (wasClosed && cnn.State != ConnectionState.Closed) cnn.Close();
                if (disposeCommand) { cmd.Parameters.Clear(); cmd.Dispose(); }
            }
        }
        public Task<object?> QueryAsync(Type type, ICacheGivingParser cache, bool disposeCommand = true, CancellationToken ct = default) {
            ArgumentNullException.ThrowIfNull(type);
            if (cmd is DbCommand c) return c.QueryAsync(type, cache, disposeCommand, ct);
            var makers = TypeParser.TypeParserMakers;
            for (int i = 0; i < makers.Count; i++)
                if (makers[i].TryColdStartAsync(type, cmd, cache, disposeCommand, ct, out var cold))
                    return cold!;
            return Task.FromResult(cmd.Query(type, cache, disposeCommand));
        }
    }
    /// <summary>Creates a command for the connection and applies the transaction and timeout.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static DbCommand GetCommand(this DbConnection cnn, DbTransaction? transaction, int? timeout) {
        var cmd = cnn.CreateCommand();
        if (transaction is not null)
            cmd.Transaction = transaction;
        if (timeout.HasValue)
            cmd.CommandTimeout = timeout.Value;
        return cmd;
    }
    /// <summary>Creates a command for the connection and applies the transaction and timeout.</summary>
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
