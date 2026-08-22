using System.Data;
using DataDbType = System.Data.DbType;

namespace RinkuPowerTools;

public class ParameterMetadata {
    public ParameterMetadata(
        string dbName,
        DbType? dbType,
        bool isNullable,
        int size,
        ParameterDirection direction,
        byte precision,
        byte scale,
        string? csharpType = null,
        ProviderParameterType? providerType = null,
        ParameterBinding binding = ParameterBinding.Named) {

        DbName = dbName;
        CleanName = ToCSharpName(dbName);
        DbType = dbType;
        IsNullable = isNullable;
        Size = size;
        Direction = direction;
        Precision = precision;
        Scale = scale;
        ProviderType = providerType;
        Binding = binding;
        CSharpType = ApplyNullability(csharpType ?? MapDbTypeToCSharpBase(dbType), isNullable);
    }

    public string DbName { get; }
    public string CleanName { get; }
    public DbType? DbType { get; set; }
    public bool IsNullable { get; set; }
    public int Size { get; set; }
    public ParameterDirection Direction { get; }
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public ProviderParameterType? ProviderType { get; set; }
    public string CSharpType { get; set; }
    public ParameterBinding Binding { get; }

    public static string MapDbTypeToCSharp(DbType? dbType, bool isNullable) =>
        ApplyNullability(MapDbTypeToCSharpBase(dbType), isNullable);

    public static string MapDbTypeToCSharpBase(DbType? dbType) => dbType switch {
        DataDbType.AnsiString or DataDbType.AnsiStringFixedLength or DataDbType.String or DataDbType.StringFixedLength or DataDbType.Xml => "string",
        DataDbType.Int32 => "int",
        DataDbType.Int64 => "long",
        DataDbType.Int16 => "short",
        DataDbType.UInt64 => "ulong",
        DataDbType.UInt32 => "uint",
        DataDbType.UInt16 => "ushort",
        DataDbType.SByte => "sbyte",
        DataDbType.Byte => "byte",
        DataDbType.Boolean => "bool",
        DataDbType.Guid => "Guid",
        DataDbType.DateTime or DataDbType.DateTime2 or DataDbType.Date => "DateTime",
        DataDbType.DateTimeOffset => "DateTimeOffset",
        DataDbType.Time => "TimeSpan",
        DataDbType.Currency or DataDbType.Decimal or DataDbType.VarNumeric => "decimal",
        DataDbType.Double => "double",
        DataDbType.Single => "float",
        DataDbType.Binary => "byte[]",
        _ => "object"
    };

    public void UpdateType(ProviderTypeInfo type, bool? isNullable = null) {
        if (isNullable is { } nullable)
            IsNullable = nullable;

        DbType = type.DbType;
        Size = type.Size;
        Precision = type.Precision;
        Scale = type.Scale;
        ProviderType = type.ProviderType;
        CSharpType = ApplyNullability(type.CSharpType, IsNullable);
    }

    public void UpdateNullability(bool isNullable) {
        IsNullable = isNullable;
        string baseType = CSharpType.EndsWith('?') ? CSharpType[..^1] : CSharpType;
        CSharpType = ApplyNullability(baseType, isNullable);
    }

    private static string ToCSharpName(string dbName) {
        ReadOnlySpan<char> source = dbName.AsSpan();
        int start = 0;
        while (start < source.Length && source[start] is '@' or '$' or ':')
            start++;
        source = source[start..];
        if (source.IsEmpty)
            return "parameter";

        var chars = new char[source.Length + (char.IsDigit(source[0]) ? 1 : 0)];
        int destination = 0;
        if (char.IsDigit(source[0]))
            chars[destination++] = 'p';

        foreach (char c in source)
            chars[destination++] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';

        return new string(chars, 0, destination);
    }

    private static string ApplyNullability(string type, bool isNullable) {
        if (!isNullable || type.EndsWith('?'))
            return type;
        return type + "?";
    }
}
