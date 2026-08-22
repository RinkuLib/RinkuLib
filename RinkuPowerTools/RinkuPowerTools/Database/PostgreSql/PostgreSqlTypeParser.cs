using System.Data;

namespace RinkuPowerTools;

internal static class PostgreSqlTypeParser {
    public static ProviderTypeInfo Parse(string rawType) {
        TryParse(rawType, out ProviderTypeInfo type);
        return type;
    }

    public static bool TryParse(string rawType, out ProviderTypeInfo type) {
        ParsedTypeDeclaration declaration = TypeDeclarationParser.Parse(rawType);
        string name = Normalize(declaration.Name);

        if (name.EndsWith("[]", StringComparison.Ordinal)) {
            if (!TryParse(name[..^2], out ProviderTypeInfo element) || element.CSharpType == "object") {
                type = new ProviderTypeInfo(
                    null,
                    "object",
                    ProviderType: new ProviderParameterType(DatabaseType.PostgreSql, declaration.Name.Trim()));
                return false;
            }

            string elementTypeName = element.ProviderType?.DataTypeName ?? name[..^2];
            type = new ProviderTypeInfo(
                null,
                element.CSharpType + "[]",
                ProviderType: new ProviderParameterType(DatabaseType.PostgreSql, elementTypeName + "[]"));
            return true;
        }

        int size = 0;
        byte precision = 0;
        byte scale = 0;
        DbType? dbType;
        string csharpType;
        string providerTypeName;

        switch (name) {
            case "smallint":
            case "int2":
                dbType = DbType.Int16;
                csharpType = "short";
                providerTypeName = "smallint";
                break;
            case "integer":
            case "int":
            case "int4":
            case "serial":
                dbType = DbType.Int32;
                csharpType = "int";
                providerTypeName = "integer";
                break;
            case "bigint":
            case "int8":
            case "bigserial":
                dbType = DbType.Int64;
                csharpType = "long";
                providerTypeName = "bigint";
                break;
            case "oid":
            case "xid":
            case "cid":
                dbType = DbType.UInt32;
                csharpType = "uint";
                providerTypeName = name;
                break;
            case "numeric":
            case "decimal":
                dbType = DbType.Decimal;
                csharpType = "decimal";
                precision = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "precision");
                scale = TypeDeclarationParser.ParseByte(declaration.SecondArgument, "scale");
                providerTypeName = "numeric";
                break;
            case "money":
                dbType = DbType.Currency;
                csharpType = "decimal";
                providerTypeName = "money";
                break;
            case "real":
            case "float4":
                dbType = DbType.Single;
                csharpType = "float";
                providerTypeName = "real";
                break;
            case "double precision":
            case "float8":
                dbType = DbType.Double;
                csharpType = "double";
                providerTypeName = "double precision";
                break;
            case "boolean":
            case "bool":
                dbType = DbType.Boolean;
                csharpType = "bool";
                providerTypeName = "boolean";
                break;
            case "text":
                dbType = DbType.String;
                csharpType = "string";
                providerTypeName = "text";
                break;
            case "character varying":
            case "varchar":
                dbType = DbType.String;
                csharpType = "string";
                size = TypeDeclarationParser.ParseSize(declaration.FirstArgument);
                providerTypeName = "character varying";
                break;
            case "character":
            case "char":
            case "bpchar":
                dbType = DbType.StringFixedLength;
                csharpType = "string";
                size = TypeDeclarationParser.ParseSize(declaration.FirstArgument);
                providerTypeName = "character";
                break;
            case "name":
                dbType = DbType.String;
                csharpType = "string";
                providerTypeName = "name";
                break;
            case "citext":
                dbType = DbType.String;
                csharpType = "string";
                providerTypeName = "citext";
                break;
            case "json":
                dbType = DbType.String;
                csharpType = "string";
                providerTypeName = "json";
                break;
            case "jsonb":
                dbType = DbType.String;
                csharpType = "string";
                providerTypeName = "jsonb";
                break;
            case "xml":
                dbType = DbType.Xml;
                csharpType = "string";
                providerTypeName = "xml";
                break;
            case "uuid":
                dbType = DbType.Guid;
                csharpType = "Guid";
                providerTypeName = "uuid";
                break;
            case "bytea":
                dbType = DbType.Binary;
                csharpType = "byte[]";
                providerTypeName = "bytea";
                break;
            case "timestamp":
            case "timestamp without time zone":
                dbType = DbType.DateTime2;
                csharpType = "DateTime";
                scale = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "scale");
                providerTypeName = "timestamp without time zone";
                break;
            case "timestamp with time zone":
            case "timestamptz":
                dbType = DbType.DateTime;
                csharpType = "DateTime";
                scale = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "scale");
                providerTypeName = "timestamp with time zone";
                break;
            case "date":
                dbType = DbType.Date;
                csharpType = "DateOnly";
                providerTypeName = "date";
                break;
            case "time":
            case "time without time zone":
                dbType = DbType.Time;
                csharpType = "TimeOnly";
                scale = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "scale");
                providerTypeName = "time without time zone";
                break;
            case "time with time zone":
            case "timetz":
                dbType = DbType.DateTimeOffset;
                csharpType = "DateTimeOffset";
                scale = TypeDeclarationParser.ParseByte(declaration.FirstArgument, "scale");
                providerTypeName = "time with time zone";
                break;
            case "interval":
                dbType = null;
                csharpType = "TimeSpan";
                providerTypeName = "interval";
                break;
            default:
                type = new ProviderTypeInfo(
                    null,
                    "object",
                    ProviderType: new ProviderParameterType(DatabaseType.PostgreSql, declaration.Name.Trim()));
                return false;
        }

        type = new ProviderTypeInfo(
            dbType,
            csharpType,
            size,
            precision,
            scale,
            new ProviderParameterType(DatabaseType.PostgreSql, providerTypeName));
        return true;
    }

    private static string Normalize(string value) {
        string name = value.Trim().ToLowerInvariant();
        const string pgCatalog = "pg_catalog.";
        if (name.StartsWith(pgCatalog, StringComparison.Ordinal))
            name = name[pgCatalog.Length..];
        return name;
    }
}
