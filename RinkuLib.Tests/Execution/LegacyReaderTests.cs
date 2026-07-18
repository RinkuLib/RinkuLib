using System.Data;
using System.Data.Common;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <c>WrappedBasicReader</c> turns a plain <see cref="IDataReader"/> into a full
/// <see cref="DbDataReader"/>, forwarding every member; <c>DisposedReader</c> is the sentinel a consumed
/// reader is swapped for, throwing on any data access.
/// </summary>
public class LegacyReaderTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    DbDataReader Wrap(string sql, out DbDataReader inner) {
        var cnn = Db.Open();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = sql;
        inner = cmd.ExecuteReader(CommandBehavior.CloseConnection);
        return new WrappedBasicReader(new PlainReader(inner));
    }

    void EnsureTypedTable() {
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Typed (I INTEGER, S TEXT, D REAL, B BLOB, C TEXT, DT TEXT, G TEXT);
            DELETE FROM Typed;
            INSERT INTO Typed VALUES (1, 'text', 2.5, X'0102', 'a', '2024-05-01 13:30:15',
                '33221100-5544-7766-8899-aabbccddeeff');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Every_typed_getter_forwards() {
        EnsureTypedTable();
        using var reader = Wrap("SELECT I, S, D, B, C, DT, G FROM Typed", out _);
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal((short)1, reader.GetInt16(0));
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal((byte)1, reader.GetByte(0));
        Assert.True(reader.GetBoolean(0));
        Assert.Equal("text", reader.GetString(1));
        Assert.Equal(2.5d, reader.GetDouble(2));
        Assert.Equal(2.5f, reader.GetFloat(2));
        Assert.Equal(2.5m, reader.GetDecimal(2));
        Assert.Equal(new DateTime(2024, 5, 1, 13, 30, 15), reader.GetDateTime(5));
        Assert.Equal(Guid.Parse("33221100-5544-7766-8899-aabbccddeeff"), reader.GetGuid(6));

        var buffer = new byte[2];
        Assert.Equal(2, reader.GetBytes(3, 0, buffer, 0, 2));
        Assert.Equal([1, 2], buffer);
        var chars = new char[1];
        Assert.Equal(1, reader.GetChars(4, 0, chars, 0, 1));
        Assert.Equal('a', chars[0]);
        Assert.Equal('a', reader.GetChar(4));

        Assert.Equal(1L, reader.GetValue(0));
        Assert.Equal(1L, reader[0]);
        Assert.Equal(1L, reader["I"]);
        Assert.Equal(1L, reader.GetFieldValue<long>(0));
        var all = new object[7];
        Assert.Equal(7, reader.GetValues(all));
        Assert.Equal("text", all[1]);
        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public void Metadata_and_lifecycle_members_forward() {
        var reader = Wrap("SELECT ID, Name FROM Users ORDER BY ID", out var inner);
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal(2, reader.VisibleFieldCount);
        Assert.Equal(0, reader.Depth);
        Assert.True(reader.HasRows);
        Assert.False(reader.IsClosed);
        Assert.Equal("ID", reader.GetName(0));
        Assert.Equal(1, reader.GetOrdinal("Name"));
        Assert.Equal(typeof(long), reader.GetFieldType(0));
        Assert.Equal("INTEGER", reader.GetDataTypeName(0));
        Assert.Equal(typeof(long), reader.GetProviderSpecificFieldType(0));
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetProviderSpecificValue(0));
        var vals = new object[2];
        Assert.Equal(2, reader.GetProviderSpecificValues(vals));
        Assert.ThrowsAny<Exception>(() => reader.GetDbDataReader_(0));
        Assert.ThrowsAny<Exception>(() => reader.InitializeLifetimeService());
        Assert.Throws<NotSupportedException>(() => reader.GetStream(1));
        Assert.Throws<NotSupportedException>(() => reader.GetTextReader(1));

        int seen = 0;
        foreach (var row in reader)
            seen++;
        Assert.Equal(2, seen);
        Assert.False(reader.NextResult());
        Assert.Equal(-1, inner.RecordsAffected);
        _ = reader.RecordsAffected;
        reader.Close();
        Assert.True(reader.IsClosed);
        reader.Dispose();
    }

    [Fact]
    public void Schema_members_build_column_schemas() {
        using var reader = Wrap("SELECT ID, Name, Salary FROM Users", out _);
        var table = reader.GetSchemaTable();
        Assert.NotNull(table);
        Assert.Equal(3, table.Rows.Count);

        var schema = ((IDbColumnSchemaGenerator)reader).GetColumnSchema();
        Assert.Equal(3, schema.Count);
        Assert.Equal("ID", schema[0].ColumnName);
    }

    [Fact]
    public async Task The_async_members_forward_synchronously() {
        var ct = TestContext.Current.CancellationToken;
        var reader = Wrap("SELECT ID FROM Users ORDER BY ID", out _);
        Assert.True(await reader.ReadAsync(ct));
        Assert.False(await reader.IsDBNullAsync(0, ct));
        Assert.Equal(1L, await reader.GetFieldValueAsync<long>(0, ct));
        Assert.NotNull(await reader.GetSchemaTableAsync(ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.GetColumnSchemaAsync(ct));
        Assert.False(await reader.NextResultAsync(ct));
        await reader.CloseAsync();
        await reader.DisposeAsync();
    }

    [Fact]
    public void A_null_inner_reader_is_refused() {
        Assert.Throws<ArgumentNullException>(() => new WrappedBasicReader(null!));
    }

    [Fact]
    public void GetFieldValue_maps_a_null_cell_and_an_enumerable_inner_uses_its_own_enumerator() {
        using var reader = Wrap("SELECT Email FROM Users ORDER BY ID", out _);
        Assert.True(reader.Read());
        Assert.Null(reader.GetFieldValue<string?>(0));

        var cnn = Db.Open();
        var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID FROM Users";
        var inner = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
        using var enumerableWrap = new WrappedBasicReader(new PlainEnumerableReader(inner));
        int rows = 0;
        foreach (var _ in enumerableWrap)
            rows++;
        Assert.Equal(3, rows);
    }

    [Fact]
    public async Task The_disposed_sentinel_reports_empty_and_throws_on_data() {
        var ct = TestContext.Current.CancellationToken;
        var d = DisposedReader.Instance;
        Assert.Equal(0, d.Depth);
        Assert.Equal(0, d.FieldCount);
        Assert.Equal(0, d.VisibleFieldCount);
        Assert.True(d.IsClosed);
        Assert.False(d.HasRows);
        Assert.Equal(-1, d.RecordsAffected);
        d.Close();
        d.Dispose();
        await d.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => d.Read());
        Assert.Throws<ObjectDisposedException>(() => d.NextResult());
        Assert.Throws<ObjectDisposedException>(() => d.GetSchemaTable());
        Assert.Throws<ObjectDisposedException>(() => d.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => d[0]);
        Assert.Throws<ObjectDisposedException>(() => d["x"]);
        Assert.Throws<ObjectDisposedException>(() => d.GetBoolean(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetByte(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetBytes(0, 0, null, 0, 0));
        Assert.Throws<ObjectDisposedException>(() => d.GetChar(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetChars(0, 0, null, 0, 0));
        Assert.Throws<ObjectDisposedException>(() => d.GetDataTypeName(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetDateTime(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetDecimal(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetDouble(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetFieldType(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetFieldValue<int>(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetFloat(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetGuid(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetInt16(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetInt32(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetInt64(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetName(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetOrdinal("x"));
        Assert.Throws<ObjectDisposedException>(() => d.GetProviderSpecificFieldType(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetProviderSpecificValue(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetProviderSpecificValues([]));
        Assert.Throws<ObjectDisposedException>(() => d.GetStream(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetString(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetTextReader(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetValue(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetValues([]));
        Assert.Throws<ObjectDisposedException>(() => d.IsDBNull(0));
        Assert.Throws<ObjectDisposedException>(() => d.GetDbDataReader_(0));
        Assert.ThrowsAny<Exception>(() => d.InitializeLifetimeService());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => d.ReadAsync(ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => d.NextResultAsync(ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => d.IsDBNullAsync(0, ct));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => d.GetFieldValueAsync<int>(0, ct));
    }
}

file static class ReaderExtensions {
    public static DbDataReader GetDbDataReader_(this DbDataReader reader, int i)
        => (DbDataReader)((IDataReader)reader).GetData(i);
}
