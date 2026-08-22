using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace RinkuPowerTools;

public sealed class SqlServerSchemaDiscoverer : SchemaDiscoverer {
    private const string DedupeMarker = "__DEDUPE__";

    public override async Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct) {

        if (cnn is not SqlConnection sqlCnn)
            throw new ArgumentException("A SQL Server connection is required.", nameof(cnn));

        await EnsureOpenAsync(sqlCnn, ct);
        string sqlText = query.SourceType == QuerySourceType.StoredProcedure
            ? query.Target
            : await settings.GetSqlTextAsync(query, ct);

        List<ParameterMetadata> parameters = query.SourceType == QuerySourceType.StoredProcedure
            ? await DiscoverStoredProcedureParametersAsync(sqlCnn, query.Target, ct)
            : await DiscoverInlineQueryParametersAsync(sqlCnn, sqlText, ct);

        ApplyUserOverrides(parameters, query.Parameters, SqlServerTypeParser.Parse);

        string discoverySql = query.SourceType == QuerySourceType.StoredProcedure
            ? $"EXEC {query.Target}"
            : sqlText;
        List<ParameterMetadata> columns = await DiscoverColumnsAsync(sqlCnn, discoverySql, parameters, ct);

        return new DiscoveredSchema(sqlText, parameters, columns);
    }

    public static async Task<IReadOnlyList<string>> GetStoredProceduresAsync(SqlConnection cnn, CancellationToken ct) {
        if (cnn.State != ConnectionState.Open)
            await cnn.OpenAsync(ct);

        using var command = new SqlCommand("""
SELECT s.name + '.' + p.name
FROM sys.procedures AS p
INNER JOIN sys.schemas AS s ON s.schema_id = p.schema_id
WHERE p.is_ms_shipped = 0
ORDER BY s.name, p.name;
""", cnn);

        var procedures = new List<string>();
        using SqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            procedures.Add(reader.GetString(0));
        return procedures;
    }

    private static async Task<List<ParameterMetadata>> DiscoverStoredProcedureParametersAsync(SqlConnection cnn, string procedureName, CancellationToken ct) {
        var parameters = new List<ParameterMetadata>();
        using var cmd = new SqlCommand(@"
SELECT 
    p.name AS ParameterName,
    CASE 
        WHEN t.is_table_type = 1 
            THEN t.name + ' READONLY'
        WHEN t.name IN ('sysname', 'text', 'ntext', 'image', 'hierarchyid', 'geometry', 'geography', 'timestamp', 'xml')
            THEN t.name
        WHEN t.name IN ('nchar', 'nvarchar') 
            THEN t.name + '(' + CASE WHEN p.max_length = -1 THEN 'max' ELSE CAST(p.max_length / 2 AS VARCHAR(10)) END + ')'
        WHEN t.name IN ('char', 'varchar', 'binary', 'varbinary') 
            THEN t.name + '(' + CASE WHEN p.max_length = -1 THEN 'max' ELSE CAST(p.max_length AS VARCHAR(10)) END + ')'
        WHEN t.name IN ('decimal', 'numeric') 
            THEN t.name + '(' + CAST(p.precision AS VARCHAR(5)) + ',' + CAST(p.scale AS VARCHAR(5)) + ')'
        WHEN t.name IN ('datetime2', 'datetimeoffset', 'time')
            THEN t.name + '(' + CAST(p.scale AS VARCHAR(5)) + ')'
        ELSE t.name
    END AS FullSqlType,
    p.is_output AS IsOutput,
    CAST(CASE WHEN p.is_nullable = 1 OR t.name IN ('image', 'text', 'ntext', 'varchar', 'nvarchar', 'varbinary') THEN 1 ELSE 0 END AS BIT) AS IsNullable
FROM sys.parameters p
INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
WHERE p.object_id = OBJECT_ID(@ProcName)
ORDER BY p.parameter_id;", cnn);
        cmd.Parameters.AddWithValue("@ProcName", procedureName);
        using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            bool nullable = reader.GetBoolean(3);
            ProviderTypeInfo type = SqlServerTypeParser.Parse(reader.GetString(1));
            parameters.Add(new ParameterMetadata(
                reader.GetString(0),
                type.DbType,
                nullable,
                type.Size,
                reader.GetBoolean(2) ? ParameterDirection.InputOutput : ParameterDirection.Input,
                type.Precision,
                type.Scale,
                type.CSharpType));
        }
        return parameters;
    }

    private static async Task<List<ParameterMetadata>> DiscoverColumnsAsync(
        SqlConnection cnn,
        string sql,
        List<ParameterMetadata> parameters,
        CancellationToken ct) {

        var declarations = new List<string>(parameters.Count);
        foreach (ParameterMetadata parameter in parameters)
            declarations.Add($"@{parameter.CleanName} {SqlServerTypeParser.MapCSharpToDeclaration(parameter.CSharpType)}");

        string paramBlock = string.Join(", ", declarations);
        using var cmd = new SqlCommand("SELECT name, system_type_name, is_nullable FROM sys.dm_exec_describe_first_result_set(@sql, @params, 0)", cnn);
        cmd.Parameters.AddWithValue("@sql", sql);
        cmd.Parameters.AddWithValue("@params", string.IsNullOrEmpty(paramBlock) ? DBNull.Value : paramBlock);

        var columns = new List<ParameterMetadata>();
        using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            if (await reader.IsDBNullAsync(0, ct))
                continue;

            bool nullable = reader.GetBoolean(2);
            ProviderTypeInfo type = SqlServerTypeParser.Parse(reader.IsDBNull(1) ? "nvarchar(max)" : reader.GetString(1));
            columns.Add(new ParameterMetadata(
                reader.GetString(0),
                type.DbType,
                nullable,
                type.Size,
                ParameterDirection.Input,
                type.Precision,
                type.Scale,
                type.CSharpType));
        }
        return columns;
    }

    private static async Task<List<ParameterMetadata>> DiscoverInlineQueryParametersAsync(SqlConnection cnn, string sqlText, CancellationToken ct) =>
        MergeDuplicateMetadata(await ExecuteAndTransformLoopAsync(cnn, sqlText, ct));

    private static async Task<List<ParameterMetadata>> ExecuteAndTransformLoopAsync(SqlConnection cnn, string sqlText, CancellationToken ct) {
        try {
            return await ExecuteParameterSnifferAsync(cnn, sqlText, ct);
        }
        catch (SqlException ex) when (ex.Number == 11508) {
            return await ExecuteAndTransformLoopAsync(cnn, DeduplicateParameterInText(sqlText, ExtractParamNameFromError(ex.Message)), ct);
        }
    }

    private static async Task<List<ParameterMetadata>> ExecuteParameterSnifferAsync(SqlConnection cnn, string sqlText, CancellationToken ct) {
        var parameters = new List<ParameterMetadata>();
        using var cmd = new SqlCommand("EXEC sp_describe_undeclared_parameters @tsql = @QueryText", cnn);
        cmd.Parameters.AddWithValue("@QueryText", sqlText);
        using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            int typeOrdinal = reader.GetOrdinal("suggested_system_type_name");
            if (await reader.IsDBNullAsync(typeOrdinal, ct))
                continue;

            ProviderTypeInfo type = SqlServerTypeParser.Parse(reader.GetString(typeOrdinal));
            parameters.Add(new ParameterMetadata(
                reader.GetString(reader.GetOrdinal("name")),
                type.DbType,
                true,
                type.Size,
                reader.GetBoolean(reader.GetOrdinal("suggested_is_output")) ? ParameterDirection.Output : ParameterDirection.Input,
                type.Precision,
                type.Scale,
                type.CSharpType));
        }
        return parameters;
    }

    private static string ExtractParamNameFromError(string errorMessage) {
        if (string.IsNullOrEmpty(errorMessage))
            throw new ArgumentException("SQL Server error message was empty or null.", nameof(errorMessage));

        int firstQuote = errorMessage.IndexOf('\'');
        int secondQuote = errorMessage.IndexOf('\'', firstQuote + 1);
        if (firstQuote >= 0 && secondQuote > firstQuote)
            return errorMessage.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

        throw new FormatException($"Could not extract duplicate parameter name from SQL Server error: {errorMessage}");
    }

    private static string DeduplicateParameterInText(string sqlText, string paramName) {
        int index = sqlText.IndexOf(paramName, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
            return sqlText;
        index = sqlText.IndexOf(paramName, index + paramName.Length, StringComparison.OrdinalIgnoreCase);

        while (index != -1) {
            string uniqueSuffix = $"{DedupeMarker}{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            sqlText = sqlText.Insert(index + paramName.Length, uniqueSuffix);
            int nextScanStart = index + paramName.Length + uniqueSuffix.Length;
            index = sqlText.IndexOf(paramName, nextScanStart, StringComparison.OrdinalIgnoreCase);
        }

        return sqlText;
    }

    private static List<ParameterMetadata> MergeDuplicateMetadata(List<ParameterMetadata> raw) {
        var result = new List<ParameterMetadata>(raw.Count);
        var indexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (ParameterMetadata metadata in raw) {
            string cleanName = metadata.DbName;
            int markerIndex = cleanName.IndexOf(DedupeMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
                cleanName = cleanName[..markerIndex];

            if (!indexMap.TryGetValue(cleanName, out int index)) {
                indexMap[cleanName] = result.Count;
                result.Add(new ParameterMetadata(
                    cleanName,
                    metadata.DbType,
                    metadata.IsNullable,
                    metadata.Size,
                    metadata.Direction,
                    metadata.Precision,
                    metadata.Scale,
                    metadata.CSharpType.TrimEnd('?')));
                continue;
            }

            ParameterMetadata existing = result[index];
            bool nullable = existing.IsNullable || metadata.IsNullable;
            int size = existing.Size == -1 || metadata.Size == -1 ? -1 : Math.Max(existing.Size, metadata.Size);
            byte precision = Math.Max(existing.Precision, metadata.Precision);
            byte scale = Math.Max(existing.Scale, metadata.Scale);
            ParameterDirection direction = existing.Direction == metadata.Direction ? existing.Direction : ParameterDirection.InputOutput;
            DbType? dbType = existing.DbType;

            if (existing.DbType != metadata.DbType && existing.DbType is DbType.Int32 or DbType.Boolean)
                dbType = metadata.DbType;

            result[index] = new ParameterMetadata(
                cleanName,
                dbType,
                nullable,
                size,
                direction,
                precision,
                scale,
                ParameterMetadata.MapDbTypeToCSharpBase(dbType));
        }

        return result;
    }
}
