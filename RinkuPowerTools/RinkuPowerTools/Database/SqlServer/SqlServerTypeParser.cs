using System.Data;

namespace RinkuPowerTools;

internal static class SqlServerTypeParser {
    public static ProviderTypeInfo Parse(string rawType) {
        string cleanType = rawType.Trim();
        if (cleanType.EndsWith(" READONLY", StringComparison.OrdinalIgnoreCase))
            cleanType = cleanType[..^9].Trim();

        int collateIndex = cleanType.IndexOf(" COLLATE ", StringComparison.OrdinalIgnoreCase);
        if (collateIndex >= 0)
            cleanType = cleanType[..collateIndex].Trim();

        ParsedTypeDeclaration declaration = TypeDeclarationParser.Parse(cleanType);
        string name = declaration.Name.ToLowerInvariant();
        DbType dbType = MapTypeName(name);
        int size = 0;
        byte precision = 0;
        byte scale = 0;

        if (name is "decimal" or "numeric") {
            precision = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "precision");
            scale = TypeDeclarationParser.ParseByte(declaration.SecondArgument, "scale");
        }
        else if (name is "datetime2" or "datetimeoffset" or "time") {
            scale = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "scale");
        }
        else if (declaration.FirstArgument is not null) {
            size = TypeDeclarationParser.ParseSize(declaration.FirstArgument);
        }

        return new ProviderTypeInfo(dbType, ParameterMetadata.MapDbTypeToCSharpBase(dbType), size, precision, scale);
    }

    public static DbType MapTypeName(string name) => name.ToLowerInvariant() switch {
        "int" => DbType.Int32,
        "bigint" => DbType.Int64,
        "smallint" => DbType.Int16,
        "tinyint" => DbType.Byte,
        "bit" => DbType.Boolean,
        "nvarchar" or "varchar" or "text" or "ntext" or "sysname" => DbType.String,
        "char" or "nchar" => DbType.StringFixedLength,
        "datetime" or "smalldatetime" or "datetime2" => DbType.DateTime2,
        "datetimeoffset" => DbType.DateTimeOffset,
        "date" => DbType.Date,
        "time" => DbType.Time,
        "decimal" or "numeric" or "money" or "smallmoney" => DbType.Decimal,
        "float" => DbType.Double,
        "real" => DbType.Single,
        "uniqueidentifier" => DbType.Guid,
        "varbinary" or "binary" or "image" or "timestamp" or "rowversion" => DbType.Binary,
        "xml" => DbType.Xml,
        "sql_variant" => DbType.Object,
        _ => DbType.Object
    };

    public static string MapCSharpToDeclaration(string csharpType) {
        if (string.IsNullOrWhiteSpace(csharpType))
            return "nvarchar(max)";

        string cleanType = csharpType.Replace("?", "", StringComparison.Ordinal).Trim().ToLowerInvariant();
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
            "timespan" => "time(7)",
            "decimal" => "decimal(38,18)",
            "double" => "float(53)",
            "float" or "single" => "real",
            "byte[]" => "varbinary(max)",
            _ => "nvarchar(max)"
        };
    }
}
