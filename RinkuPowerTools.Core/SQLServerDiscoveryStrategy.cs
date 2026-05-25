using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace RinkuPowerTools.Core;

#pragma warning disable CS0618
public class SQLServerDiscoveryStrategy : ISchemaDiscovererStrategy {
    private const string DedupeMarker = "__DEDUPE__";

    public static async Task<DiscoveredSchema> DiscoverSchemaAsync(DbConnection cnn, QuerySetting query, List<ParameterOverride> userOverrides, string projectDirectory, CancellationToken ct) {
        var sqlCnn = (SqlConnection)cnn;

        var rawParams = query.SourceType == QuerySourceType.StoredProcedure
            ? await DiscoverStoredProcedureParametersAsync(sqlCnn, query.Target, ct)
            : await DiscoverInlineQueryParametersAsync(sqlCnn, await GetSqlText(query, projectDirectory, ct), ct);

        var finalParams = ISchemaDiscovererStrategy.ApplyUserOverrides(rawParams, userOverrides);
        string sqlPayload = query.SourceType == QuerySourceType.StoredProcedure ? $"EXEC {query.Target}" : await GetSqlText(query, projectDirectory, ct);
        var columns = await DiscoverColumnsAsync(sqlCnn, sqlPayload, finalParams, ct);

        return new DiscoveredSchema(query.SourceType == QuerySourceType.StoredProcedure ? query.Target : sqlPayload, finalParams, columns);
    }

    private static async Task<string> GetSqlText(QuerySetting query, string projectDirectory, CancellationToken ct) =>
        query.SourceType == QuerySourceType.FromFile
            ? await File.ReadAllTextAsync(Path.IsPathRooted(query.Target) ? query.Target : Path.Combine(projectDirectory, query.Target), ct)
            : query.Target;

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
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            var meta = new ParameterMetadata(reader.GetString(0), DbType.Object, reader.GetBoolean(3), 0, reader.GetBoolean(2) ? ParameterDirection.InputOutput : ParameterDirection.Input, 0, 0);
            meta.UpdateFromSqlType(reader.GetString(1), meta.IsNullable);
            parameters.Add(meta);
        }
        return parameters;
    }

    private static async Task<List<ParameterMetadata>> DiscoverColumnsAsync(SqlConnection cnn, string sql, List<ParameterMetadata> paramsList, CancellationToken ct) {
        var declarations = paramsList.Select(p => $"@{p.CleanName} {ParameterMetadata.MapCSharpToSqlDeclaration(p.CSharpType)}");
        string paramBlock = string.Join(", ", declarations);
        using var cmd = new SqlCommand("SELECT name, system_type_name, is_nullable FROM sys.dm_exec_describe_first_result_set(@sql, @params, 0)", cnn);
        cmd.Parameters.AddWithValue("@sql", sql);
        cmd.Parameters.AddWithValue("@params", string.IsNullOrEmpty(paramBlock) ? DBNull.Value : paramBlock);

        var columns = new List<ParameterMetadata>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            if (await reader.IsDBNullAsync(0, ct))
                continue;
            var meta = new ParameterMetadata(reader.GetString(0), DbType.Object, reader.GetBoolean(2), 0, ParameterDirection.Input, 0, 0);
            meta.UpdateFromSqlType(reader.IsDBNull(1) ? "nvarchar(max)" : reader.GetString(1), meta.IsNullable);
            columns.Add(meta);
        }
        return columns;
    }

    private static async Task<List<ParameterMetadata>> DiscoverInlineQueryParametersAsync(SqlConnection cnn, string sqlText, CancellationToken ct) =>
        MergeDuplicateMetadata(await ExecuteAndTransformLoopAsync(cnn, sqlText, ct));

    private static async Task<List<ParameterMetadata>> ExecuteAndTransformLoopAsync(SqlConnection cnn, string sqlText, CancellationToken ct) {
        try { return await ExecuteParameterSnifferAsync(cnn, sqlText, ct); }
        catch (SqlException ex) when (ex.Number == 11508) {
            return await ExecuteAndTransformLoopAsync(cnn, DeduplicateParameterInText(sqlText, ExtractParamNameFromError(ex.Message)), ct);
        }
    }

    private static async Task<List<ParameterMetadata>> ExecuteParameterSnifferAsync(SqlConnection cnn, string sqlText, CancellationToken ct) {
        var parameters = new List<ParameterMetadata>();
        using var cmd = new SqlCommand("EXEC sp_describe_undeclared_parameters @tsql = @QueryText", cnn);
        cmd.Parameters.AddWithValue("@QueryText", sqlText);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            if (await reader.IsDBNullAsync(reader.GetOrdinal("suggested_system_type_name"), ct))
                continue;
            var meta = new ParameterMetadata(reader.GetString(reader.GetOrdinal("name")), DbType.Object, true, 0,
                reader.GetBoolean(reader.GetOrdinal("suggested_is_output")) ? ParameterDirection.Output : ParameterDirection.Input, 0, 0);
            meta.UpdateFromSqlType(reader.GetString(reader.GetOrdinal("suggested_system_type_name")), true);
            parameters.Add(meta);
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
            string uniqueSuffix = $"{DedupeMarker}{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
            sqlText = sqlText.Insert(index + paramName.Length, uniqueSuffix);

            int nextScanStart = index + paramName.Length + uniqueSuffix.Length;
            index = sqlText.IndexOf(paramName, nextScanStart, StringComparison.OrdinalIgnoreCase);
        }

        return sqlText;
    }
    private static List<ParameterMetadata> MergeDuplicateMetadata(List<ParameterMetadata> raw) {
        var res = new List<ParameterMetadata>(raw.Count);
        var indexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in raw) {
            string cleanName = m.DbName;
            int markerIdx = cleanName.IndexOf(DedupeMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx > 0)
                cleanName = cleanName[..markerIdx];

            if (indexMap.TryGetValue(cleanName, out int idx)) {
                var existing = res[idx];

                bool mergedNullable = existing.IsNullable || m.IsNullable;

                int mergedSize = (existing.Size == -1 || m.Size == -1)
                    ? -1
                    : Math.Max(existing.Size, m.Size);

                byte mergedPrecision = Math.Max(existing.Precision, m.Precision);
                byte mergedScale = Math.Max(existing.Scale, m.Scale);

                ParameterDirection mergedDirection = existing.Direction == m.Direction
                    ? existing.Direction
                    : ParameterDirection.InputOutput;

                DbType mergedType = existing.DbType;

                if (existing.DbType != m.DbType) {
                    if (existing.DbType == DbType.Int32 || existing.DbType == DbType.Boolean) {
                        mergedType = m.DbType;
                    }
                }

                res[idx] = new ParameterMetadata(
                    cleanName,
                    mergedType,
                    mergedNullable,
                    mergedSize,
                    mergedDirection,
                    mergedPrecision,
                    mergedScale
                );
            }
            else {
                indexMap[cleanName] = res.Count;
                res.Add(new ParameterMetadata(
                    cleanName, m.DbType, m.IsNullable, m.Size, m.Direction, m.Precision, m.Scale));
            }
        }

        return res;
    }
}
#pragma warning restore CS0618