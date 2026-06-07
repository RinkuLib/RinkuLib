using System.Data;

namespace RinkuPowerTools;
public class ParameterMetadata(string dbName, DbType dbType, bool isNullable, int size, ParameterDirection direction, byte precision, byte scale) {
    public string DbName { get; } = dbName;
    public string CleanName { get; } = dbName.TrimStart(['@', '$', ':']).Replace(" ", "_");
    public DbType DbType { get; set; } = dbType;
    public bool IsNullable { get; set; } = isNullable;
    public int Size { get; set; } = size;
    public ParameterDirection Direction { get; } = direction;
    public byte Precision { get; set; } = precision;
    public byte Scale { get; set; } = scale;
    public string CSharpType { get; set; } = MapDbTypeToCSharp(dbType, isNullable);

    public static string MapDbTypeToCSharp(DbType dbType, bool isNullable) {
        string baseType = dbType switch {
            DbType.AnsiString or DbType.AnsiStringFixedLength or DbType.String or DbType.StringFixedLength => "string",
            DbType.Int32 => "int",
            DbType.Int64 => "long",
            DbType.Int16 => "short",
            DbType.Byte => "byte",
            DbType.Boolean => "bool",
            DbType.Guid => "Guid",
            DbType.DateTime or DbType.DateTime2 or DbType.Date => "DateTime",
            DbType.DateTimeOffset => "DateTimeOffset",
            DbType.Decimal => "decimal",
            DbType.Double => "double",
            DbType.Single => "float",
            DbType.Binary => "byte[]",
            _ => "object"
        };

        return isNullable ? baseType + "?" : baseType;
    }

    public static DbType MapSqlTypeNameToDbType(string sqlTypeName) {
        return sqlTypeName.ToLowerInvariant() switch {
            "int" => DbType.Int32,
            "bigint" => DbType.Int64,
            "smallint" => DbType.Int16,
            "tinyint" => DbType.Byte,
            "bit" => DbType.Boolean,
            "nvarchar" or "varchar" or "text" or "ntext" or "sysname" => DbType.String,
            "char" or "nchar" => DbType.StringFixedLength,
            "datetime" or "datetime2" or "datetimeoffset" => DbType.DateTime2,
            "date" => DbType.Date,
            "decimal" or "numeric" or "money" or "smallmoney" => DbType.Decimal,
            "uniqueidentifier" => DbType.Guid,
            "varbinary" or "binary" or "image" => DbType.Binary,
            _ => DbType.Object
        };
    }

    /// <summary>
    /// Inverts a C# type string parsed from source code back into an explicit, compilation-safe SQL declaration type.
    /// </summary>
    public static string MapCSharpToSqlDeclaration(string csharpType) {
        if (string.IsNullOrWhiteSpace(csharpType))
            return "nvarchar(max)";

        string cleanType = csharpType.Replace("?", "").Trim().ToLowerInvariant();

        return cleanType switch {
            "string" => "nvarchar(max)",
            "int" or "int32" => "int",
            "long" or "int64" => "bigint",
            "short" or "int16" => "smallint",
            "byte" => "tinyint",
            "bool" or "boolean" => "bit",
            "guid" => "uniqueidentifier",
            "datetime" => "datetime2(7)",
            "datetimeoffset" => "datetimeoffset(7)",
            "decimal" => "decimal(38,18)",
            "double" => "float(53)",
            "float" or "single" => "real",
            "byte[]" => "varbinary(max)",
            _ => "nvarchar(max)"
        };
    }

    /// <summary>
    /// Deeply parses a SQL data type declaration (e.g. "nvarchar(max)", "decimal(18,2)", "datetime2(3)", "varchar(10) collate Latin1_General...")
    /// </summary>
    public void UpdateFromSqlType(string? rawSqlTypeName, bool? isNullable) {
        if (isNullable.HasValue)
            IsNullable = isNullable.Value;

        if (string.IsNullOrWhiteSpace(rawSqlTypeName))
            return;

        string cleanSqlType = rawSqlTypeName.Trim();

        if (cleanSqlType.EndsWith(" READONLY", StringComparison.OrdinalIgnoreCase))
            cleanSqlType = cleanSqlType[..^9].Trim();

        int collateIdx = cleanSqlType.IndexOf(" COLLATE ", StringComparison.OrdinalIgnoreCase);
        if (collateIdx >= 0)
            cleanSqlType = cleanSqlType[..collateIdx].Trim();

        var match = SharedRegex.SQLTypeRegex().Match(cleanSqlType);

        Size = 0;
        Precision = 0;
        Scale = 0;

        if (match.Success) {
            string baseTypeName = match.Groups[1].Value;
            string firstParam = match.Groups[2].Value;
            string secondParam = match.Groups[3].Value;

            DbType = MapSqlTypeNameToDbType(baseTypeName);

            if (!string.IsNullOrEmpty(firstParam)) {
                if (firstParam.Equals("max", StringComparison.OrdinalIgnoreCase)) {
                    Size = -1;
                }
                else if (int.TryParse(firstParam, out int firstNum)) {
                    if (!string.IsNullOrEmpty(secondParam) && byte.TryParse(secondParam, out byte parsedScale)) {
                        Precision = (byte)firstNum;
                        Scale = parsedScale;
                    }
                    else if (baseTypeName.Contains("date", StringComparison.OrdinalIgnoreCase) ||
                             baseTypeName.Equals("time", StringComparison.OrdinalIgnoreCase)) {
                        Scale = (byte)firstNum;
                    }
                    else {
                        Size = firstNum;
                    }
                }
            }
        }

        CSharpType = MapDbTypeToCSharp(DbType, IsNullable);
    }
}