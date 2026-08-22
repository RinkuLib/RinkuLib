using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace RinkuPowerTools;

public sealed class SqliteSchemaDiscoverer : SchemaDiscoverer {
    public override async Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct) {

        if (cnn is not SqliteConnection sqliteCnn)
            throw new ArgumentException("A SQLite connection is required.", nameof(cnn));
        if (query.SourceType == QuerySourceType.StoredProcedure)
            throw new NotSupportedException("SQLite does not support stored procedures. Use a SQL query or SQL file.");

        await EnsureOpenAsync(sqliteCnn, ct);
        string sql = await settings.GetSqlTextAsync(query, ct);
        List<ParameterMetadata> parameters = DiscoverParameters(sql);
        ApplyUserOverrides(parameters, query.Parameters, SqliteTypeParser.Parse);
        List<ParameterMetadata> columns = await DiscoverColumnsAsync(sqliteCnn, sql, parameters, ct);
        return new DiscoveredSchema(sql, parameters, columns);
    }

    private static List<ParameterMetadata> DiscoverParameters(string sql) {
        List<string> names = NamedParameterScanner.Scan(sql);
        var parameters = new List<ParameterMetadata>(names.Count);
        foreach (string name in names)
            parameters.Add(new ParameterMetadata(name, null, true, 0, ParameterDirection.Input, 0, 0, "object"));
        return parameters;
    }

    private static Task<List<ParameterMetadata>> DiscoverColumnsAsync(
        SqliteConnection cnn,
        string sql,
        List<ParameterMetadata> parameters,
        CancellationToken ct) {

        ct.ThrowIfCancellationRequested();
        _ = parameters;

        int result = raw.sqlite3_prepare_v2(cnn.Handle, sql, out sqlite3_stmt statement);
        if (result != raw.SQLITE_OK)
            throw new InvalidOperationException(
                $"SQLite could not prepare the query for schema discovery: {raw.sqlite3_errmsg(cnn.Handle).utf8_to_string()}");

        try {
            int columnCount = raw.sqlite3_column_count(statement);
            var columns = new List<ParameterMetadata>(columnCount);
            for (int index = 0; index < columnCount; index++) {
                string name = raw.sqlite3_column_name(statement, index).utf8_to_string() ?? string.Empty;
                string? typeName = raw.sqlite3_column_decltype(statement, index).utf8_to_string();
                bool nullable = true;

                string? databaseName = raw.sqlite3_column_database_name(statement, index).utf8_to_string();
                string? tableName = raw.sqlite3_column_table_name(statement, index).utf8_to_string();
                string? originName = raw.sqlite3_column_origin_name(statement, index).utf8_to_string();
                if (!string.IsNullOrEmpty(tableName) && !string.IsNullOrEmpty(originName)) {
                    int metadataResult = raw.sqlite3_table_column_metadata(
                        cnn.Handle,
                        databaseName ?? "main",
                        tableName,
                        originName,
                        out string metadataType,
                        out _,
                        out int notNull,
                        out int primaryKey,
                        out _);
                    if (metadataResult == raw.SQLITE_OK) {
                        typeName ??= metadataType;
                        nullable = notNull == 0 && primaryKey == 0;
                    }
                }

                ProviderTypeInfo parsed = typeName is not null &&
                    SqliteTypeParser.TryParse(typeName, out ProviderTypeInfo parsedType)
                        ? parsedType
                        : new ProviderTypeInfo(null, "object");

                columns.Add(new ParameterMetadata(
                    name,
                    parsed.DbType,
                    nullable,
                    parsed.Size,
                    ParameterDirection.Input,
                    parsed.Precision,
                    parsed.Scale,
                    parsed.CSharpType));
            }

            return Task.FromResult(columns);
        }
        finally {
            raw.sqlite3_finalize(statement);
        }
    }
}
