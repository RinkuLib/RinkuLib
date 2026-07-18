using System.Collections;
using System.Data;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// A bare <see cref="IDbCommand"/> whose parameters are plain <see cref="IDbDataParameter"/> objects, not
/// <see cref="System.Data.Common.DbParameter"/>, to exercise the paths that handle legacy ADO providers.
/// </summary>
public sealed class LegacyCommand : IDbCommand {
    public LegacyParameterCollection Parameters { get; } = new();
    IDataParameterCollection IDbCommand.Parameters => Parameters;
    public string CommandText { get; set; } = "";
    public int CommandTimeout { get; set; }
    public CommandType CommandType { get; set; }
    public IDbConnection? Connection { get; set; }
    public IDbTransaction? Transaction { get; set; }
    public UpdateRowSource UpdatedRowSource { get; set; }
    public IDbDataParameter CreateParameter() => new LegacyParameter();
    public void Cancel() { }
    public void Dispose() { }
    public void Prepare() { }
    public int ExecuteNonQuery() => 0;
    public IDataReader ExecuteReader() => throw new NotSupportedException();
    public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
    public object? ExecuteScalar() => null;
}

/// <summary>A plain <see cref="IDbDataParameter"/> with no <c>DbParameter</c> base.</summary>
public sealed class LegacyParameter : IDbDataParameter {
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public int Size { get; set; }
    public DbType DbType { get; set; }
    public ParameterDirection Direction { get; set; }
    public bool IsNullable => false;
    public string? ParameterName { get; set; }
    public string? SourceColumn { get; set; }
    public DataRowVersion SourceVersion { get; set; }
    public object? Value { get; set; }
}

public sealed class LegacyParameterCollection : IDataParameterCollection {
    public readonly List<object?> Items = [];
    public int Count => Items.Count;
    public bool IsFixedSize => false;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public object? this[int index] { get => Items[index]; set => Items[index] = value; }
    public object? this[string parameterName] {
        get => Items.FirstOrDefault(p => (p as IDataParameter)?.ParameterName == parameterName);
        set { }
    }
    public int Add(object? value) { Items.Add(value); return Items.Count - 1; }
    public void Clear() => Items.Clear();
    public bool Contains(object? value) => Items.Contains(value);
    public bool Contains(string parameterName) => IndexOf(parameterName) >= 0;
    public void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);
    public IEnumerator GetEnumerator() => Items.GetEnumerator();
    public int IndexOf(object? value) => Items.IndexOf(value);
    public int IndexOf(string parameterName) => Items.FindIndex(p => (p as IDataParameter)?.ParameterName == parameterName);
    public void Insert(int index, object? value) => Items.Insert(index, value);
    public void Remove(object? value) => Items.Remove(value);
    public void RemoveAt(int index) => Items.RemoveAt(index);
    public void RemoveAt(string parameterName) { var i = IndexOf(parameterName); if (i >= 0) Items.RemoveAt(i); }
}
