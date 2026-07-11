using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;

/// <summary>
/// Reads a command's several result sets in turn, mapping each to a type you pick as you go. Call
/// <see cref="Query{T}"/> once per set to take the whole set and move on, or <see cref="Get{T}"/> and
/// <see cref="GetCurrentSetParser{T}"/> to walk a set row by row while keeping hold of the reader. It is itself a
/// <see cref="DbDataReader"/>, so the raw reader methods stay available underneath.
/// </summary>
public sealed class MultiReader(bool[] usage, QueryCommand command, DbDataReader reader, IDbCommand cmd, bool disposeCmd, bool wasClosed) : DbDataReader, IDisposable {
    private readonly bool[] usage = usage;
    private readonly QueryCommand command = command;
    private readonly DbDataReader reader = reader;
    private readonly IDbCommand cmd = cmd;
    private readonly bool disposeCmd = disposeCmd;
    private readonly bool wasClosed = wasClosed;
    private int nbResultSetPassedMinusOne = -1;
    /// <summary>
    /// Parses starting at the current row in the current result set, does not change result set.
    /// The parser advances the reader, and <c>CanContinue</c> reports its state on return,
    /// <see langword="true"/> when it is left on an untreated row
    /// </summary>
    public (bool CanContinue, T Result) Get<T>() => GetCurrentSetParser<T>().Parse(reader);
    /// <inheritdoc cref="Get{T}"/>
    /// <param name="ct">The fowarded cancellation token</param>
    public async ValueTask<(bool CanContinue, T Result)> GetAsync<T>(CancellationToken ct = default)
        => await GetCurrentSetParser<T>().ParseAsync(reader, ct).ConfigureAwait(false);
    /// <summary>The current set's parser, to parse rows yourself while keeping control of the reader.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ITypeParser<T> GetCurrentSetParser<T>() {
        if (command.TryGetCachedParser<T>(usage, out var cache, nbResultSetPassedMinusOne))
            return cache;
        var schema = reader.GetColumns();
        cache = TypeParser.GetTypeParser<T>(ref schema);
        command.UpdateParseCache(usage, schema, cache, nbResultSetPassedMinusOne);
        return cache;
    }
    /// <summary>
    /// Automaticaly skip non-returning set, parse the current result set to return an instance of <typeparamref name="T"/> or the default if no result, then move to the next result set.
    /// To parse a set row by row and keep control of the reader, use <see cref="Get{T}"/> or <see cref="GetCurrentSetParser{T}"/>
    /// </summary>
    public T Query<T>() {
        while (reader.FieldCount == 0)
            reader.NextResult();
        nbResultSetPassedMinusOne++;
        bool goToNextResultSet = true;
        try {
            var cache = GetCurrentSetParser<T>();
            if (!reader.Read())
                return cache.Default();
            if (cache is ILazyTypeParser<T> lazyParser) {
                goToNextResultSet = false;
                return lazyParser.ParseAndOwn<GoToNextResultSet>(reader, new());
            }
            if (cache is ISimpleParser<T> simple)
                return simple.RowParser(reader);
            return cache.Parse(reader).Result;
        }
        finally {
            if (goToNextResultSet)
                reader.NextResult();
        }
    }
    /// <summary>
    /// Asynchronously, automaticaly skip non-returning set, parse the current result set to return an instance of <typeparamref name="T"/> or the default if no result, then move to the next result set.
    /// To parse a set row by row and keep control of the reader, use <see cref="GetAsync{T}"/> or <see cref="GetCurrentSetParser{T}"/>
    /// </summary>
    /// <param name="ct">The fowarded cancellation token</param>
    public async Task<T> QueryAsync<T>(CancellationToken ct = default) {
        while (reader.FieldCount == 0)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
        nbResultSetPassedMinusOne++;
        bool goToNextResultSet = true;
        try {
            var cache = GetCurrentSetParser<T>();
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return cache.Default();
            if (cache is ILazyTypeParser<T> lazyParser) {
                goToNextResultSet = false;
                return lazyParser.ParseAndOwn<GoToNextResultSet>(reader, new());
            }
            if (cache is ISimpleParser<T> simple)
                return simple.RowParser(reader);
            return (await cache.ParseAsync(reader, ct).ConfigureAwait(false)).Result;
        }
        finally {
            if (goToNextResultSet)
                await reader.NextResultAsync(ct).ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Asynchronously, automaticaly skip non-returning set, lazily parse the rows in that result set and go to next result once enumeration completes.
    /// </summary>
    /// <param name="goToNextResultSet">Indicate if the reader should move to the next result set once enumeration completes</param>
    /// <param name="ct">The fowarded cancellation token</param>
    public async IAsyncEnumerable<T> StreamQueryAsync<T>(bool goToNextResultSet = true, [EnumeratorCancellation] CancellationToken ct = default) {
        while (reader.FieldCount == 0)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
        nbResultSetPassedMinusOne++;
        var cache = GetCurrentSetParser<T>();
        if (cache is ISimpleParser<T> simple) {
            var rowParser = simple.RowParser;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                yield return rowParser(reader);
        }
        else if (await reader.ReadAsync(ct).ConfigureAwait(false)) {
            bool canContinue;
            do {
                (canContinue, var item) = await cache.ParseAsync(reader, ct).ConfigureAwait(false);
                yield return item;
            } while (canContinue);
        }
        if (goToNextResultSet)
            await reader.NextResultAsync(ct).ConfigureAwait(false);
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