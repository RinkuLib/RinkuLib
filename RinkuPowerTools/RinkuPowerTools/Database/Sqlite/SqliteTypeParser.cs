using System.Data;

namespace RinkuPowerTools;

internal static class SqliteTypeParser {
    public static ProviderTypeInfo Parse(string rawType) {
        if (TryParse(rawType, out ProviderTypeInfo type))
            return type;
        throw new InvalidOperationException($"Unsupported SQLite type declaration '{rawType}'.");
    }

    public static bool TryParse(string rawType, out ProviderTypeInfo type) {
        ParsedTypeDeclaration declaration = TypeDeclarationParser.Parse(rawType);
        string name = declaration.Name.Trim().ToLowerInvariant();
        int size = TypeDeclarationParser.ParseSize(declaration.FirstArgument);

        switch (name) {
            case "integer":
            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
                type = new ProviderTypeInfo(DbType.Int64, "long");
                return true;
            case "real":
            case "double":
            case "float":
                type = new ProviderTypeInfo(DbType.Double, "double");
                return true;
            case "text":
            case "varchar":
            case "nvarchar":
            case "char":
            case "nchar":
            case "clob":
                type = new ProviderTypeInfo(DbType.String, "string", size);
                return true;
            case "blob":
            case "binary":
            case "varbinary":
                type = new ProviderTypeInfo(DbType.Binary, "byte[]", size);
                return true;
            case "numeric":
            case "decimal":
                type = new ProviderTypeInfo(DbType.Decimal, "decimal");
                return true;
            case "boolean":
            case "bool":
                type = new ProviderTypeInfo(DbType.Boolean, "bool");
                return true;
            case "date":
                type = new ProviderTypeInfo(DbType.Date, "DateOnly");
                return true;
            case "datetime":
            case "timestamp":
                type = new ProviderTypeInfo(DbType.DateTime2, "DateTime");
                return true;
            case "datetimeoffset":
                type = new ProviderTypeInfo(DbType.DateTimeOffset, "DateTimeOffset");
                return true;
            case "time":
                type = new ProviderTypeInfo(DbType.Time, "TimeOnly");
                return true;
            case "timespan":
                type = new ProviderTypeInfo(DbType.Time, "TimeSpan");
                return true;
            case "guid":
            case "uuid":
                type = new ProviderTypeInfo(DbType.Guid, "Guid");
                return true;
        }

        // SQLite declared types use affinity rules. This keeps common custom declarations useful.
        string upper = declaration.Name.ToUpperInvariant();
        if (upper.Contains("INT", StringComparison.Ordinal)) {
            type = new ProviderTypeInfo(DbType.Int64, "long");
            return true;
        }
        if (upper.Contains("CHAR", StringComparison.Ordinal) || upper.Contains("CLOB", StringComparison.Ordinal) || upper.Contains("TEXT", StringComparison.Ordinal)) {
            type = new ProviderTypeInfo(DbType.String, "string", size);
            return true;
        }
        if (upper.Contains("BLOB", StringComparison.Ordinal)) {
            type = new ProviderTypeInfo(DbType.Binary, "byte[]", size);
            return true;
        }
        if (upper.Contains("REAL", StringComparison.Ordinal) || upper.Contains("FLOA", StringComparison.Ordinal) || upper.Contains("DOUB", StringComparison.Ordinal)) {
            type = new ProviderTypeInfo(DbType.Double, "double");
            return true;
        }

        type = new ProviderTypeInfo(null, "object");
        return false;
    }
}
