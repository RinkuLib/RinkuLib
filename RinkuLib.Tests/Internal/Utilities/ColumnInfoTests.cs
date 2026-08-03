using System.Collections;
using System.Collections.ObjectModel;
using System.Data.Common;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Utilities;

/// <summary>
/// <see cref="ColumnInfo"/> and its helpers: schema extraction from a reader, structural comparison, the
/// content-based array comparer, and the duplicate-name mapper.
/// </summary>
public class ColumnInfoTests {
    [Fact]
    public void Array_comparer_compares_contents() {
        var comparer = ArrayContentComparer<int>.Instance;
        int[] a = [1, 2, 3];
        Assert.True(comparer.Equals(a, a));
        Assert.False(comparer.Equals(a, null));
        Assert.False(comparer.Equals(null, a));
        Assert.True(comparer.Equals(a, [1, 2, 3]));
        Assert.False(comparer.Equals(a, [1, 2, 4]));
        Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode([1, 2, 3]));
        Assert.NotEqual(comparer.GetHashCode(a), comparer.GetHashCode([1, 2, 4]));
    }

    [Fact]
    public void MakeMapper_without_duplicates_returns_the_plain_mapper() {
        ColumnInfo[] cols = [new("ID", typeof(int), false), new("Name", typeof(string), true)];
        using var mapper = cols.MakeMapper();
        Assert.Equal(2, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("ID"));
        Assert.Equal(1, mapper.GetIndex("Name"));
    }

    [Fact]
    public void MakeMapper_suffixes_duplicate_names() {
        ColumnInfo[] cols = [new("ID", typeof(int), false), new("Name", typeof(string), true), new("ID", typeof(long), true)];
        using var mapper = cols.MakeMapper();
        Assert.Equal(3, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("ID"));
        Assert.Equal(1, mapper.GetIndex("Name"));
        Assert.Equal(2, mapper.GetIndex("ID#2"));
    }

    [Fact]
    public void MakeMapper_walks_past_a_taken_suffix() {
        ColumnInfo[] cols = [new("ID", typeof(int), false), new("ID#2", typeof(int), false), new("ID", typeof(long), true)];
        using var mapper = cols.MakeMapper();
        Assert.Equal(3, mapper.Count);
        Assert.Equal(0, mapper.GetIndex("ID"));
        Assert.Equal(1, mapper.GetIndex("ID#2"));
        Assert.Equal(2, mapper.GetIndex("ID#3"));
    }

    [Fact]
    public void GetColumns_reads_the_schema_and_defaults_the_holes() {
        var reader = new SchemaOnlyReader([
            new TestColumn("ID", typeof(int), allowNull: false, isAliased: true, isExpression: false),
            new TestColumn(null, null, allowNull: null, isAliased: null, isExpression: null),
            new TestColumn("Total + 1", typeof(long), allowNull: true, isAliased: false, isExpression: true),
            new TestColumn("Alias", typeof(long), allowNull: true, isAliased: true, isExpression: true),
        ]);
        var cols = reader.GetColumns();
        Assert.Equal(new ColumnInfo("ID", typeof(int), false).Name, cols[0].Name);
        Assert.False(cols[0].IsNullable);
        Assert.Equal("", cols[1].Name);
        Assert.Equal(typeof(object), cols[1].Type);
        Assert.True(cols[1].IsNullable);
        Assert.Equal("", cols[2].Name);
        Assert.Equal("Alias", cols[3].Name);
    }

    [Fact]
    public void GetColumnsFast_reads_names_and_types_and_defaults_the_holes() {
        var reader = new SchemaOnlyReader([
            new TestColumn("ID", typeof(int), false, true, false),
            new TestColumn(null, null, null, null, null),
        ]);
        var cols = reader.GetColumnsFast();
        Assert.Equal("ID", cols[0].Name);
        Assert.Equal(typeof(int), cols[0].Type);
        Assert.True(cols[0].IsNullable);
        Assert.Equal("", cols[1].Name);
        Assert.Equal(typeof(object), cols[1].Type);
    }

    [Fact]
    public void GetColumnsFast_with_no_columns_is_empty() {
        var reader = new SchemaOnlyReader([]);
        Assert.Empty(reader.GetColumnsFast());
    }

    sealed class TestColumn : DbColumn {
        public TestColumn(string? name, Type? type, bool? allowNull, bool? isAliased, bool? isExpression) {
            ColumnName = name!;
            DataType = type;
            AllowDBNull = allowNull;
            IsAliased = isAliased;
            IsExpression = isExpression;
        }
    }

    sealed class SchemaOnlyReader(DbColumn[] Schema) : DbDataReader, IDbColumnSchemaGenerator {
        public ReadOnlyCollection<DbColumn> GetColumnSchema() => new(Schema);
        public override int FieldCount => Schema.Length;
        public override string GetName(int ordinal) => Schema[ordinal].ColumnName;
        public override Type GetFieldType(int ordinal) => Schema[ordinal].DataType!;

        public override object this[int ordinal] => throw new NotSupportedException();
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;
        public override bool HasRows => false;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override int GetOrdinal(string name) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override object GetValue(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override bool Read() => false;
    }
}
