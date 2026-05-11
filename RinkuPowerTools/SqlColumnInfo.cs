using System.Data;

namespace RinkuPowerTools;

public readonly record struct SqlColumnInfo(string Name, SqlDbType DataType, bool IsNullable, int Size) {
    private static readonly Dictionary<SqlDbType, string> TypeMap = new() {
        [SqlDbType.BigInt] = "long",
        [SqlDbType.Binary] = "byte[]",
        [SqlDbType.Bit] = "bool",
        [SqlDbType.Char] = "string",
        [SqlDbType.DateTime] = "DateTime",
        [SqlDbType.Decimal] = "decimal",
        [SqlDbType.Float] = "double",
        [SqlDbType.Image] = "byte[]",
        [SqlDbType.Int] = "int",
        [SqlDbType.Money] = "decimal",
        [SqlDbType.NChar] = "string",
        [SqlDbType.NText] = "string",
        [SqlDbType.NVarChar] = "string",
        [SqlDbType.Real] = "float",
        [SqlDbType.UniqueIdentifier] = "Guid",
        [SqlDbType.SmallDateTime] = "DateTime",
        [SqlDbType.SmallInt] = "short",
        [SqlDbType.SmallMoney] = "decimal",
        [SqlDbType.Text] = "string",
        [SqlDbType.Timestamp] = "byte[]",
        [SqlDbType.TinyInt] = "byte",
        [SqlDbType.VarBinary] = "byte[]",
        [SqlDbType.VarChar] = "string",
        [SqlDbType.Variant] = "object",
        [SqlDbType.Xml] = "string",
        [SqlDbType.Udt] = "object",
        [SqlDbType.Structured] = "DataTable",
        [SqlDbType.Date] = "DateTime",
        [SqlDbType.Time] = "TimeSpan",
        [SqlDbType.DateTime2] = "DateTime",
        [SqlDbType.DateTimeOffset] = "DateTimeOffset"
    };
    public SqlColumnInfo(string name, string rawSqlTypeName, bool isNullable, int size)
        : this(name, ParseSqlDbType(rawSqlTypeName), isNullable, size) { }
    public string CSharpTypeName {
        get {
            if (!TypeMap.TryGetValue(DataType, out var typeName))
                typeName = "object";

            return IsNullable ? $"{typeName}?" : typeName;
        }
    }
    public string? GetParameterSizeString() {
        return DataType switch {
            SqlDbType.NVarChar or SqlDbType.VarChar or
            SqlDbType.Binary or SqlDbType.VarBinary or
            SqlDbType.Char or SqlDbType.NChar => Size.ToString(),
            _ => null
        };
    }
    private static SqlDbType ParseSqlDbType(string rawSqlTypeName) {
        if (string.IsNullOrEmpty(rawSqlTypeName))
            return SqlDbType.Variant;

        int parenIndex = rawSqlTypeName.IndexOf('(');
        ReadOnlySpan<char> typeSpan = parenIndex == -1
            ? rawSqlTypeName.AsSpan()
            : rawSqlTypeName.AsSpan(0, parenIndex);

        if (Enum.TryParse<SqlDbType>(typeSpan, ignoreCase: true, out var result))
            return result;

        return SqlDbType.Variant;
    }
}