using System.Data;
using System.Data.Common;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// An <see cref="IDataReader"/> that is deliberately not a <see cref="DbDataReader"/>, delegating to a real
/// one, to drive the legacy-provider roads (<c>WrappedBasicReader</c>).
/// </summary>
public sealed class PlainReader(DbDataReader inner) : IDataReader {
    public object this[int i] => inner[i];
    public object this[string name] => inner[name];
    public int Depth => inner.Depth;
    public bool IsClosed => inner.IsClosed;
    public int RecordsAffected => inner.RecordsAffected;
    public int FieldCount => inner.FieldCount;
    public void Close() => inner.Close();
    public void Dispose() => inner.Dispose();
    public bool GetBoolean(int i) => inner.GetBoolean(i);
    public byte GetByte(int i) => inner.GetByte(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => inner.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
    public char GetChar(int i) => inner.GetChar(i);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => inner.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => inner.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => inner.GetDateTime(i);
    public decimal GetDecimal(int i) => inner.GetDecimal(i);
    public double GetDouble(int i) => inner.GetDouble(i);
    public Type GetFieldType(int i) => inner.GetFieldType(i);
    public float GetFloat(int i) => inner.GetFloat(i);
    public Guid GetGuid(int i) => inner.GetGuid(i);
    public short GetInt16(int i) => inner.GetInt16(i);
    public int GetInt32(int i) => inner.GetInt32(i);
    public long GetInt64(int i) => inner.GetInt64(i);
    public string GetName(int i) => inner.GetName(i);
    public int GetOrdinal(string name) => inner.GetOrdinal(name);
    public DataTable? GetSchemaTable() => inner.GetSchemaTable();
    public string GetString(int i) => inner.GetString(i);
    public object GetValue(int i) => inner.GetValue(i);
    public int GetValues(object[] values) => inner.GetValues(values);
    public bool IsDBNull(int i) => inner.IsDBNull(i);
    public bool NextResult() => inner.NextResult();
    public bool Read() => inner.Read();
}

/// <summary>
/// A <see cref="PlainReader"/> variant that is also <see cref="System.Collections.IEnumerable"/>, for the
/// wrapper road that defers to the inner reader's own enumerator.
/// </summary>
public sealed class PlainEnumerableReader(DbDataReader inner) : IDataReader, System.Collections.IEnumerable {
    private readonly PlainReader _plain = new(inner);
    public System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
    public object this[int i] => _plain[i];
    public object this[string name] => _plain[name];
    public int Depth => _plain.Depth;
    public bool IsClosed => _plain.IsClosed;
    public int RecordsAffected => _plain.RecordsAffected;
    public int FieldCount => _plain.FieldCount;
    public void Close() => _plain.Close();
    public void Dispose() => _plain.Dispose();
    public bool GetBoolean(int i) => _plain.GetBoolean(i);
    public byte GetByte(int i) => _plain.GetByte(i);
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => _plain.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
    public char GetChar(int i) => _plain.GetChar(i);
    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => _plain.GetChars(i, fieldoffset, buffer, bufferoffset, length);
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => _plain.GetDataTypeName(i);
    public DateTime GetDateTime(int i) => _plain.GetDateTime(i);
    public decimal GetDecimal(int i) => _plain.GetDecimal(i);
    public double GetDouble(int i) => _plain.GetDouble(i);
    public Type GetFieldType(int i) => _plain.GetFieldType(i);
    public float GetFloat(int i) => _plain.GetFloat(i);
    public Guid GetGuid(int i) => _plain.GetGuid(i);
    public short GetInt16(int i) => _plain.GetInt16(i);
    public int GetInt32(int i) => _plain.GetInt32(i);
    public long GetInt64(int i) => _plain.GetInt64(i);
    public string GetName(int i) => _plain.GetName(i);
    public int GetOrdinal(string name) => _plain.GetOrdinal(name);
    public DataTable? GetSchemaTable() => _plain.GetSchemaTable();
    public string GetString(int i) => _plain.GetString(i);
    public object GetValue(int i) => _plain.GetValue(i);
    public int GetValues(object[] values) => _plain.GetValues(values);
    public bool IsDBNull(int i) => _plain.IsDBNull(i);
    public bool NextResult() => _plain.NextResult();
    public bool Read() => _plain.Read();
}

/// <summary>
/// A connection whose <c>Open</c> fails and which reports itself <see cref="ConnectionState.Broken"/>
/// afterwards rather than closed, the shape the cleanup paths guard against when they close a connection
/// they opened. <see cref="Closed"/> records whether that recovery ran.
/// </summary>
public sealed class BrokenConnection : DbConnection {
    public bool Closed;
    public override string? ConnectionString { get; set; }
    public override string Database => "Broken";
    public override string DataSource => "None";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => ConnectionState.Broken;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => Closed = true;
    public override void Open() => throw new InvalidOperationException("open refused");
    public override Task OpenAsync(CancellationToken ct) => throw new InvalidOperationException("open refused");
    protected override DbCommand CreateDbCommand() => new FakeCommand { Connection = this };
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();
}

/// <summary>
/// An <see cref="IDbCommand"/> that is not a <see cref="DbCommand"/> yet hands back a real
/// <see cref="DbDataReader"/>, the provider shape whose reader must be used as-is rather than wrapped.
/// </summary>
public sealed class PassthroughCommand(DbCommand inner) : IDbCommand {
    public string CommandText { get => inner.CommandText; set => inner.CommandText = value!; }
    public int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
    public CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
    public IDbConnection? Connection { get => inner.Connection; set => inner.Connection = (DbConnection?)value; }
    public IDataParameterCollection Parameters => inner.Parameters;
    public IDbTransaction? Transaction { get => inner.Transaction; set => inner.Transaction = (DbTransaction?)value; }
    public UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
    public void Cancel() => inner.Cancel();
    public IDbDataParameter CreateParameter() => inner.CreateParameter();
    public void Dispose() => inner.Dispose();
    public int ExecuteNonQuery() => inner.ExecuteNonQuery();
    public IDataReader ExecuteReader() => inner.ExecuteReader();
    public IDataReader ExecuteReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
    public object? ExecuteScalar() => inner.ExecuteScalar();
    public void Prepare() => inner.Prepare();
}

/// <summary>An <see cref="IDbCommand"/> whose connection is a <see cref="BrokenConnection"/>.</summary>
public sealed class BrokenPlainCommand(BrokenConnection cnn) : IDbCommand {
    public string? CommandText { get; set; }
    public int CommandTimeout { get; set; }
    public CommandType CommandType { get; set; }
    public IDbConnection? Connection { get => cnn; set { } }
    public IDataParameterCollection Parameters { get; } = new LegacyParameterCollection();
    public IDbTransaction? Transaction { get; set; }
    public UpdateRowSource UpdatedRowSource { get; set; }
    public void Cancel() { }
    public IDbDataParameter CreateParameter() => new LegacyParameter();
    public void Dispose() { }
    public int ExecuteNonQuery() => 0;
    public IDataReader ExecuteReader() => throw new NotSupportedException();
    public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
    public object? ExecuteScalar() => null;
    public void Prepare() { }
}

/// <summary>
/// An <see cref="IDbConnection"/> that is deliberately not a <see cref="DbConnection"/>, delegating to a
/// real one and handing out <see cref="PlainCommand"/>s, to drive every legacy-provider road end to end.
/// </summary>
public sealed class PlainConnection(DbConnection inner) : IDbConnection {
    public DbConnection Inner => inner;
    public string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value!; }
    public int ConnectionTimeout => inner.ConnectionTimeout;
    public string Database => inner.Database;
    public ConnectionState State => inner.State;
    public IDbTransaction BeginTransaction() => inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    public void Close() => inner.Close();
    public IDbCommand CreateCommand() => new PlainCommand(inner.CreateCommand());
    public void Dispose() => inner.Dispose();
    public void Open() => inner.Open();
}

/// <summary>
/// An <see cref="IDbCommand"/> that is deliberately not a <see cref="DbCommand"/>, delegating to a real one,
/// whose readers come back as plain <see cref="IDataReader"/>s.
/// </summary>
public sealed class PlainCommand(DbCommand inner) : IDbCommand {
    public DbCommand Inner => inner;
    public string CommandText { get => inner.CommandText; set => inner.CommandText = value!; }
    public int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
    public CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
    public IDbConnection? Connection { get => inner.Connection; set => inner.Connection = (DbConnection?)value; }
    public IDataParameterCollection Parameters => inner.Parameters;
    public IDbTransaction? Transaction { get => inner.Transaction; set => inner.Transaction = (DbTransaction?)value; }
    public UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
    public void Cancel() => inner.Cancel();
    public IDbDataParameter CreateParameter() => inner.CreateParameter();
    public void Dispose() => inner.Dispose();
    public int ExecuteNonQuery() => inner.ExecuteNonQuery();
    public IDataReader ExecuteReader() => new PlainReader(inner.ExecuteReader());
    public IDataReader ExecuteReader(CommandBehavior behavior) => new PlainReader(inner.ExecuteReader(behavior));
    public object? ExecuteScalar() => inner.ExecuteScalar();
    public void Prepare() => inner.Prepare();
}
