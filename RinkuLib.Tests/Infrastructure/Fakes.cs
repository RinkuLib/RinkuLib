using System.Collections;
using System.Data;
using System.Data.Common;

namespace RinkuLib.Tests.Infrastructure;

#pragma warning disable CS8764
/// <summary>A parameter that only records what was set on it.</summary>
public class FakeParameter : DbParameter {
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string? ParameterName { get; set; }
    public override int Size { get; set; }
    public override string? SourceColumn { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }
    public override void ResetDbType() { }
}

public class FakeParameterCollection : DbParameterCollection {
    public readonly List<FakeParameter> Items = [];

    public override int Count => Items.Count;
    public override object SyncRoot => this;
    public override int Add(object value) {
        Items.Add((FakeParameter)value);
        return Items.Count - 1;
    }
    public override void AddRange(Array values) {
        foreach (var val in values)
            Add(val!);
    }
    public override void Clear() => Items.Clear();
    public override bool Contains(object value) => Items.Contains((FakeParameter)value);
    public override bool Contains(string value) => Items.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => Items.GetEnumerator();
    public override int IndexOf(object value) => Items.IndexOf((FakeParameter)value);
    public override int IndexOf(string value) => Items.FindIndex(p => p.ParameterName == value);
    public override void Insert(int index, object value) => Items.Insert(index, (FakeParameter)value);
    public override void Remove(object value) => Items.Remove((FakeParameter)value);
    public override void RemoveAt(int index) => Items.RemoveAt(index);
    public override void RemoveAt(string parameterName) {
        var index = IndexOf(parameterName);
        if (index >= 0)
            Items.RemoveAt(index);
    }
    protected override DbParameter GetParameter(int index) => Items[index];
    protected override DbParameter GetParameter(string parameterName)
        => Items.FirstOrDefault(p => p.ParameterName == parameterName)!;
    protected override void SetParameter(int index, DbParameter value) => Items[index] = (FakeParameter)value;
    protected override void SetParameter(string parameterName, DbParameter value) {
        var index = IndexOf(parameterName);
        if (index >= 0)
            Items[index] = (FakeParameter)value;
        else
            Add(value);
    }
}

/// <summary>A command that records its text and parameters and never talks to a database.</summary>
public class FakeCommand : DbCommand {
    public new readonly FakeParameterCollection Parameters = [];
    public List<FakeParameter> BoundParameters => Parameters.Items;

    public override string? CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => Parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new FakeParameter();
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => null;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => throw new NotSupportedException("FakeCommand records SQL, it does not execute it.");
}

public class FakeConnection : DbConnection {
    public override string? ConnectionString { get; set; }
    public override string Database => "FakeDb";
    public override string DataSource => "None";
    public override string ServerVersion => "0.0";
    public override ConnectionState State => ConnectionState.Open;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    protected override DbCommand CreateDbCommand() => new FakeCommand { Connection = this };
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();
}
#pragma warning restore CS8764
