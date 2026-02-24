using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.Commands;

/// <summary>
/// A <see cref="DbDataReader"/> that keep track of the current result set and its associated possible mapping
/// </summary>
public sealed class MultiReader(bool[] usage, QueryCommand command, DbDataReader reader, IDbCommand cmd, bool disposeCmd, bool wasClosed) : DbDataReader, IDisposable {
    private readonly bool[] usage = usage;
    private readonly QueryCommand command = command;
    private readonly DbDataReader reader = reader;
    private readonly IDbCommand cmd = cmd;
    private readonly bool disposeCmd = disposeCmd;
    private readonly bool wasClosed = wasClosed;
    private int nbResultSetPassedMinusOne = -1;
    /// <summary>Parse the current row in the current result set, does not read or change result set</summary>
    public T? Get<T>() {
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        return cache.Parse(reader);
    }
    /// <summary>Automaticaly skip non-returning set, parse the first row in that result set and go to next result directly (ignore orher rows)</summary>
    public T? QueryOne<T>() {
        while (reader.FieldCount == 0)
            reader.NextResult();
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        T? res = default;
        if (reader.Read())
            res = cache.Parse(reader);
        reader.NextResult();
        return res;
    }
    /// <summary>Automaticaly skip non-returning set, parse the first row in that result set and go to next result directly (ignore orher rows)</summary>
    public async Task<T?> QueryOneAsync<T>(CancellationToken ct = default) {
        while (reader.FieldCount == 0)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        T? res = default;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            res = cache.Parse(reader);
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        return res;
    }
    /// <summary>Automaticaly skip non-returning set, parse the rows in that result set and go to next result after enumeration</summary>
    public IEnumerable<T> QueryAll<T>() {
        while (reader.FieldCount == 0)
            reader.NextResult();
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        while (reader.Read())
            yield return cache.Parse(reader);
        reader.NextResult();
    }
    /// <summary>Automaticaly skip non-returning set, parse the rows in that result set and go to next result after enumeration</summary>
    public async IAsyncEnumerable<T> QueryAllAsync<T>([EnumeratorCancellation] CancellationToken ct = default) {
        while (reader.FieldCount == 0)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            yield return cache.Parse(reader);
        await reader.NextResultAsync(ct).ConfigureAwait(false);
    }
    /// <summary>Automaticaly skip non-returning set, parse the rows in that result set and go to next result after enumeration</summary>
    public List<T> QueryAllBuffered<T>() {
        while (reader.FieldCount == 0)
            reader.NextResult();
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        List<T> res = [];
        while (reader.Read())
            res.Add(cache.Parse(reader));
        reader.NextResult();
        return res;
    }
    /// <summary>Automaticaly skip non-returning set, parse the rows in that result set and go to next result after enumeration</summary>
    public async Task<List<T>> QueryAllBufferedAsync<T>(CancellationToken ct = default) {
        while (reader.FieldCount == 0)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
        nbResultSetPassedMinusOne++;
        if (!command.TryGetCache<T>(usage, out var cache, nbResultSetPassedMinusOne)) {
            var schema = reader.GetColumns();
            cache = new(TypeParser<T>.GetParserFunc(ref schema, out var behavior), behavior);
            command.UpdateCache(usage.GetFalseIndexes(), schema, cache, nbResultSetPassedMinusOne);
        }
        List<T> res = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            res.Add(cache.Parse(reader));
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        return res;
    }
    /// <inheritdoc/>
    public override bool NextResult() {
        if (reader.FieldCount > 0)
            nbResultSetPassedMinusOne++;
        return reader.NextResult();
    }
    /// <inheritdoc/>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) {
        if (reader.FieldCount > 0)
            nbResultSetPassedMinusOne++;
        return reader.NextResultAsync(cancellationToken);
    }
    /// <inheritdoc/>
    public override async ValueTask DisposeAsync() {
        if (!reader.IsClosed) {
            try { cmd.Cancel(); }
            catch { }
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        if (disposeCmd) {
            cmd.Parameters.Clear();
            cmd.Dispose();
        }
        if (wasClosed) {
            if (cmd.Connection is DbConnection c)
                await c.CloseAsync().ConfigureAwait(false);
            else
                cmd.Connection?.Close();
        }
    }
    /// <inheritdoc/>
    public new void Dispose() {
        if (!reader.IsClosed) {
            try { cmd.Cancel(); }
            catch { }
        }
        reader.Dispose();
        if (disposeCmd) {
            cmd.Parameters.Clear();
            cmd.Dispose();
        }
        if (wasClosed)
            cmd.Connection?.Close();
    }
    #region Implementation
    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (disposing)
            Dispose();
    }
    /// <inheritdoc/>
    public override object this[int ordinal] => reader[ordinal];
    /// <inheritdoc/>
    public override object this[string name] => reader[name];
    /// <inheritdoc/>
    public override int Depth => reader.Depth;
    /// <inheritdoc/>
    public override int FieldCount => reader.FieldCount;
    /// <inheritdoc/>
    public override bool HasRows => reader.HasRows;
    /// <inheritdoc/>
    public override bool IsClosed => reader.IsClosed;
    /// <inheritdoc/>
    public override int RecordsAffected => reader.RecordsAffected;
    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => reader.GetByte(ordinal);
    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => reader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    /// <inheritdoc/>
    public override char GetChar(int ordinal) => reader.GetChar(ordinal);
    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => reader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => reader.GetDataTypeName(ordinal);
    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => reader.GetDecimal(ordinal);
    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => reader.GetDouble(ordinal);
    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => reader.GetEnumerator();
    /// <inheritdoc/>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => reader.GetFieldType(ordinal);
    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => reader.GetFloat(ordinal);
    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => reader.GetInt16(ordinal);
    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => reader.GetInt32(ordinal);
    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => reader.GetInt64(ordinal);
    /// <inheritdoc/>
    public override string GetName(int ordinal) => reader.GetName(ordinal);
    /// <inheritdoc/>
    public override int GetOrdinal(string name) => reader.GetOrdinal(name);
    /// <inheritdoc/>
    public override string GetString(int ordinal) => reader.GetString(ordinal);
    /// <inheritdoc/>
    public override object GetValue(int ordinal) => reader.GetValue(ordinal);
    /// <inheritdoc/>
    public override int GetValues(object[] values) => reader.GetValues(values);
    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
    /// <inheritdoc/>
    public override bool Read() => reader.Read();
    /// <inheritdoc/>
    public override void Close() => reader.Close();
    /// <inheritdoc/>
    public override Task CloseAsync() => reader.CloseAsync();
    /// <inheritdoc/>
    void IDisposable.Dispose() => Dispose();
    /// <inheritdoc/>
    public override DataTable? GetSchemaTable() => reader.GetSchemaTable();
    /// <inheritdoc/>
    public override Task<ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(CancellationToken cancellationToken = default) => reader.GetColumnSchemaAsync(cancellationToken);
    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal) => reader.GetFieldValue<T>(ordinal);
    /// <inheritdoc/>
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
    /// <inheritdoc/>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetProviderSpecificFieldType(int ordinal) => reader.GetProviderSpecificFieldType(ordinal);
    /// <inheritdoc/>
    public override object GetProviderSpecificValue(int ordinal) => reader.GetProviderSpecificValue(ordinal);
    /// <inheritdoc/>
    public override int GetProviderSpecificValues(object[] values) => reader.GetProviderSpecificValues(values);
    /// <inheritdoc/>
    public override Task<DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default) => reader.GetSchemaTableAsync(cancellationToken);
    /// <inheritdoc/>
    public override Stream GetStream(int ordinal) => reader.GetStream(ordinal);
    /// <inheritdoc/>
    public override TextReader GetTextReader(int ordinal) => reader.GetTextReader(ordinal);
    /// <inheritdoc/>
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => reader.IsDBNullAsync(ordinal, cancellationToken);
    /// <inheritdoc/>
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);
    /// <inheritdoc/>
    public override int VisibleFieldCount => reader.VisibleFieldCount;
    /// <inheritdoc/>
    public override string? ToString() => reader.ToString();
    #endregion
}