using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using RinkuLib.Tools;
using Xunit.Internal;

namespace RinkuLib.Tests;

#pragma warning disable CS8764, CA2211
public class DummyParameter : DbParameter {
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string? ParameterName { get; set; }
    public override int Size { get; set; }
    public override string? SourceColumn { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}
public class DummyParameterCollection : DbParameterCollection {
    public readonly List<DummyParameter> _parameters = [];

    public override int Count => _parameters.Count;
    public override object SyncRoot => null!;
    public override int Add(object value) {
        _parameters.Add((DummyParameter)value);
        return _parameters.Count - 1;
    }
    public override void AddRange(Array values) {
        foreach (var val in values)
            Add(val!);
    }
    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((DummyParameter)value);
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    public override int IndexOf(object value) => _parameters.IndexOf((DummyParameter)value);
    public override void Insert(int index, object value) => _parameters.Insert(index, (DummyParameter)value);
    public override void Remove(object value) => _parameters.Remove((DummyParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    // Better string-based lookup
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    public override int IndexOf(string value) => _parameters.FindIndex(p => p.ParameterName == value);
    public override void RemoveAt(string parameterName) {
        var index = IndexOf(parameterName);
        if (index >= 0)
            _parameters.RemoveAt(index);
    }
    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName)
        => _parameters.FirstOrDefault(p => p.ParameterName == parameterName)!;
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = (DummyParameter)value;
    protected override void SetParameter(string parameterName, DbParameter value) {
        var index = IndexOf(parameterName);
        if (index >= 0)
            _parameters[index] = (DummyParameter)value;
        else
            Add(value);
    }
}
public class DummyCommand : DbCommand {
    public ColumnInfo[]? ColumnsForReader;
    public Random? Random;
    public override string? CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    public new DummyParameterCollection Parameters = [];
    public List<DummyParameter> ParametersList => Parameters._parameters;
    protected override DbParameterCollection DbParameterCollection { get => Parameters; } 
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new DummyParameter();
    public override int ExecuteNonQuery() => 0;
    public override object ExecuteScalar() => null!;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) {
        if (ColumnsForReader is null)
            throw new ArgumentException($"This is a testing db command and needs to have {nameof(ColumnsForReader)} set to a value for the dummy reader to be used");
        if (Random is null)
            throw new ArgumentException($"This is a testing db command and needs to have {nameof(Random)} set to a value for the dummy reader to be used");

        var rng = Random;

        int rowCount;
        if (behavior.HasFlag(CommandBehavior.SchemaOnly))
            rowCount = 0;
        else if (behavior.HasFlag(CommandBehavior.SingleRow))
            rowCount = rng.Next(0, 10) == 0 ? 0 : 1;
        else
            rowCount = rng.Next(0, 51);

        return new SchemaDataReader(ColumnsForReader, rowCount, behavior.HasFlag(CommandBehavior.SequentialAccess));
    }
    public static DbDataReader ExecuteDbDataReader(CommandBehavior behavior, ColumnInfo[] columnsForReader, Random rng, int forcedRowCount = -1) {
        int rowCount = forcedRowCount;
        if (rowCount == -1)
            rowCount = GetRowCountFromBehavior(behavior, rng);

        return new SchemaDataReader(columnsForReader, rowCount, behavior.HasFlag(CommandBehavior.SequentialAccess));
    }

    private static int GetRowCountFromBehavior(CommandBehavior behavior, Random rng) {
        int rowCount;
        if (behavior.HasFlag(CommandBehavior.SchemaOnly))
            rowCount = 0;
        else if (behavior.HasFlag(CommandBehavior.SingleRow))
            rowCount = rng.Next(0, 10) == 0 ? 0 : 1;
        else
            rowCount = rng.Next(0, 51);
        return rowCount;
    }
}

public class DummyConnection : DbConnection {
    public ColumnInfo[]? ColumnsForReader;
    public static Random Random = new();
    public override string? ConnectionString { get; set; }
    public override string Database => "DummyDB";
    public override string DataSource => "None";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    protected override DbCommand CreateDbCommand() => CreateDummyCommand();
    public DummyCommand CreateDummyCommand() => new() { Connection = this, ColumnsForReader = ColumnsForReader };
    public DummyCommand CreateDummyCommand(ColumnInfo[] columnsForReader) => new() { Connection = this, ColumnsForReader = columnsForReader };
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotImplementedException();
}
public class SchemaDataReader : DbDataReader, IDbColumnSchemaGenerator {
    private readonly int _maxRows;
    private readonly ColumnInfo[] _columns;

    private int _currentRowIndex = 0;
    private int _lastReadOrdinal = -1;
    private readonly object[]? _currentRowData;
    private object? _currentItem;

    public SchemaDataReader(ColumnInfo[] columns, int rowCount, bool sequencial) {
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        _maxRows = rowCount;
        if (sequencial)
            _currentRowData = new object[_columns.Length];
    }

    public override bool Read() {
        if (_currentRowIndex >= _maxRows)
            return false;
        _currentItem = null;
        if (_currentRowData is not null)
            for (int i = 0; i < _columns.Length; i++) 
                _currentRowData[i] = GenerateRandomValue(_columns[i]);
        _currentRowIndex++;
        _lastReadOrdinal = -1;
        return true;
    }

    private static object GenerateRandomValue(ColumnInfo col) {
        var rng = DummyConnection.Random;

        if (col.IsNullable && rng.Next(0, 5) == 0)
            return DBNull.Value;

        var t = col.Type;

        return t switch {
            _ when t == typeof(string) => $"Text_{rng.Next(100, 999)}",
            _ when t == typeof(int) => rng.Next(1, 10000),
            _ when t == typeof(long) => (long)rng.Next() << 32 | (long)rng.Next(),
            _ when t == typeof(short) => (short)rng.Next(short.MinValue, short.MaxValue),
            _ when t == typeof(byte) => (byte)rng.Next(0, 255),
            _ when t == typeof(bool) => rng.Next(2) == 0,
            _ when t == typeof(DateTime) => DateTime.Now.AddDays(-rng.Next(1, 1000)),
            _ when t == typeof(DateTimeOffset) => DateTimeOffset.Now.AddDays(-rng.Next(1, 1000)),
            _ when t == typeof(TimeSpan) => TimeSpan.FromSeconds(rng.Next(0, 86400)),
            _ when t == typeof(decimal) => (decimal)(rng.NextDouble() * 1000),
            _ when t == typeof(double) => rng.NextDouble() * 1000,
            _ when t == typeof(float) => (float)(rng.NextDouble() * 1000),
            _ when t == typeof(Guid) => Guid.NewGuid(),
            _ when t == typeof(char) => (char)rng.Next(65, 90), // A-Z
            _ when t == typeof(byte[]) => GenerateRandomBytes(16),
            _ => throw new NotSupportedException($"Type {t.Name} is not implemented in the dummy generator.")
        };
    }

    private static byte[] GenerateRandomBytes(int length) {
        var bytes = new byte[length];
        DummyConnection.Random.NextBytes(bytes);
        return bytes;
    }

    // --- Specific Get Implementations ---
    // These throw InvalidCastException if the data is DBNull, matching real SqlClient behavior.

    public override bool GetBoolean(int ordinal) => (bool)Get(ordinal);
    public override byte GetByte(int ordinal) => (byte)Get(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)Get(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)Get(ordinal);
    public override double GetDouble(int ordinal) => (double)Get(ordinal);
    public override float GetFloat(int ordinal) => (float)Get(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)Get(ordinal);
    public override short GetInt16(int ordinal) => (short)Get(ordinal);
    public override int GetInt32(int ordinal) => (int)Get(ordinal);
    public override long GetInt64(int ordinal) => (long)Get(ordinal);
    public override string GetString(int ordinal) => (string)Get(ordinal);
    public override char GetChar(int ordinal) => (char)Get(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) {
        // We call Get with throwWhenNull: true because you can't GetBytes on a NULL
        var val = Get(ordinal);
        if (val is not byte[] data)
            throw new InvalidCastException($"Column {ordinal} is not a byte array.");

        if (buffer == null)
            return data.Length;

        int available = data.Length - (int)dataOffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(available, length);
        Array.Copy(data, (int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) {
        var val = Get(ordinal);
        string s = val switch {
            string str => str,
            char c => c.ToString(),
            _ => throw new InvalidCastException($"Column {ordinal} is not a string or char.")
        };

        if (buffer == null)
            return s.Length;

        int available = s.Length - (int)dataOffset;
        if (available <= 0)
            return 0;

        int count = Math.Min(available, length);
        s.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    private object Get(int ordinal, bool throwWhenNull = true, bool isNullCheck = false) {
        object val;
        if (_currentRowData is not null)
            val = _currentRowData[ordinal];
        else {
            if (isNullCheck ? _lastReadOrdinal > ordinal :_lastReadOrdinal >= ordinal)
                throw new InvalidOperationException(
            $"Sequential read violation: Column {ordinal} was requested, but the last read column was {_lastReadOrdinal}. " +
            "In sequential mode, you can only read forward.");
            val = _lastReadOrdinal == ordinal && _currentItem is not null
                ? _currentItem
                : _currentItem = GenerateRandomValue(_columns[ordinal]);
        }
        _lastReadOrdinal = ordinal;
        if (throwWhenNull && val == DBNull.Value)
            throw new InvalidCastException($"Column '{_columns[ordinal].Name}' is null. Check IsDBNull first.");
        return val;
    }

    // --- Essential Overrides ---
    public override bool IsDBNull(int ordinal) => Get(ordinal, false, true) == DBNull.Value;
    public override object GetValue(int ordinal) => Get(ordinal, false);
    public override int FieldCount => _columns.Length;
    public override string GetName(int ordinal) => _columns[ordinal].Name;
    public override int GetOrdinal(string name) => Array.FindIndex(_columns, c => c.Name == name);
    public override Type GetFieldType(int ordinal) => _columns[ordinal].Type;
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    // --- Boilerplate ---
    public override bool HasRows => _maxRows > 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool NextResult() => false;
    public override int GetValues(object[] values) {
        int count = Math.Min(values.Length, _columns.Length);
        if (_currentRowData is not null)
            Array.Copy(_currentRowData, values, count);
        else
            for (int i = 0; i < count; i++)
                values[i] = GetValue(i);
        _lastReadOrdinal = count;
        return count;
    }
    public override System.Collections.IEnumerator GetEnumerator() {
        return new System.Data.Common.DbEnumerator(this);
    }

    public ReadOnlyCollection<DbColumn> GetColumnSchema() {
        var schema = new List<DbColumn>();
        for (int i = 0; i < _columns.Length; i++)
            schema.Add(new DummyDbCol(_columns[i], i));
        return schema.AsReadOnly();
    }
}
public class DummyDbCol : DbColumn {
    public DummyDbCol(ColumnInfo col, int i) {
        ColumnName = col.Name;
        AllowDBNull = col.IsNullable;
        DataType = col.Type;
        ColumnOrdinal = i;
    }
}
#pragma warning restore CS8764, CA2211