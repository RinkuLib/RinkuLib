using System.Data;
using System.Data.Common;
using Npgsql;

namespace RinkuPowerTools;

public sealed class PostgreSqlSchemaDiscoverer : SchemaDiscoverer {
    public override async Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct) {

        if (cnn is not NpgsqlConnection pgCnn)
            throw new ArgumentException("A PostgreSQL connection is required.", nameof(cnn));

        await EnsureOpenAsync(pgCnn, ct);

        if (query.SourceType == QuerySourceType.StoredProcedure)
            return await DiscoverStoredProcedureAsync(pgCnn, query, ct);

        string sql = await settings.GetSqlTextAsync(query, ct);
        List<ParameterMetadata> parameters = DiscoverTextParameters(pgCnn, sql);
        ApplyUserOverrides(parameters, query.Parameters, PostgreSqlTypeParser.Parse);
        List<ParameterMetadata> columns = await DiscoverTextColumnsAsync(pgCnn, sql, parameters, ct);
        return new DiscoveredSchema(sql, parameters, columns);
    }

    public static async Task<IReadOnlyList<string>> GetStoredProceduresAsync(NpgsqlConnection cnn, CancellationToken ct) {
        if (cnn.State != ConnectionState.Open)
            await cnn.OpenAsync(ct);

        await using var command = new NpgsqlCommand("""
SELECT n.nspname || '.' || p.proname
FROM pg_proc AS p
INNER JOIN pg_namespace AS n ON n.oid = p.pronamespace
WHERE p.prokind = 'p'
  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
GROUP BY n.nspname, p.proname
ORDER BY n.nspname, p.proname;
""", cnn);

        var procedures = new List<string>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            procedures.Add(reader.GetString(0));
        return procedures;
    }

    private static async Task<DiscoveredSchema> DiscoverStoredProcedureAsync(
        NpgsqlConnection cnn,
        QuerySetting query,
        CancellationToken ct) {

        List<ParameterMetadata> parameters = await DiscoverStoredProcedureParametersAsync(cnn, query.Target, ct);
        ApplyUserOverrides(parameters, query.Parameters, PostgreSqlTypeParser.Parse);

        var columns = new List<ParameterMetadata>();
        foreach (ParameterMetadata parameter in parameters) {
            if (parameter.Direction is not (ParameterDirection.Output or ParameterDirection.InputOutput))
                continue;

            columns.Add(new ParameterMetadata(
                parameter.DbName,
                parameter.DbType,
                true,
                parameter.Size,
                ParameterDirection.Input,
                parameter.Precision,
                parameter.Scale,
                parameter.CSharpType.TrimEnd('?'),
                parameter.ProviderType));
        }

        return new DiscoveredSchema(query.Target, parameters, columns);
    }

    private static List<ParameterMetadata> DiscoverTextParameters(NpgsqlConnection cnn, string sql) {
        PostgreSqlParameterLayout layout = PostgreSqlParameterLayout.Parse(sql);
        using var command = new NpgsqlCommand(sql, cnn);
        try {
            NpgsqlCommandBuilder.DeriveParameters(command);
        }
        catch (InvalidOperationException) {
            return CreateUnknownParameters(layout);
        }
        catch (PostgresException) {
            return CreateUnknownParameters(layout);
        }
        catch (NpgsqlException) {
            return CreateUnknownParameters(layout);
        }

        if (command.Parameters.Count != layout.Names.Count)
            throw new InvalidOperationException($"PostgreSQL derived {command.Parameters.Count} parameters but the SQL contains {layout.Names.Count} parameter placeholders.");

        var parameters = new List<ParameterMetadata>(command.Parameters.Count);
        for (int i = 0; i < command.Parameters.Count; i++) {
            NpgsqlParameter parameter = command.Parameters[i];
            ProviderTypeInfo type = GetParameterType(parameter);
            parameters.Add(new ParameterMetadata(
                layout.Names[i],
                type.DbType,
                true,
                parameter.Size != 0 ? parameter.Size : type.Size,
                parameter.Direction,
                parameter.Precision != 0 ? parameter.Precision : type.Precision,
                parameter.Scale != 0 ? parameter.Scale : type.Scale,
                type.CSharpType,
                type.ProviderType,
                layout.IsPositional ? ParameterBinding.Positional : ParameterBinding.Named));
        }
        return parameters;
    }

    private static ProviderTypeInfo GetParameterType(NpgsqlParameter parameter) {
        string? dataTypeName = parameter.DataTypeName ?? parameter.PostgresType?.DisplayName;
        if (!string.IsNullOrWhiteSpace(dataTypeName)) {
            if (PostgreSqlTypeParser.TryParse(dataTypeName, out ProviderTypeInfo parsed))
                return parsed with {
                    ProviderType = new ProviderParameterType(DatabaseType.PostgreSql, dataTypeName)
                };

            DbType? customDbType = parameter.DbType == DbType.Object ? null : parameter.DbType;
            return new ProviderTypeInfo(
                customDbType,
                ParameterMetadata.MapDbTypeToCSharpBase(customDbType),
                ProviderType: new ProviderParameterType(DatabaseType.PostgreSql, dataTypeName));
        }

        DbType? dbType = parameter.DbType == DbType.Object ? null : parameter.DbType;
        return new ProviderTypeInfo(dbType, ParameterMetadata.MapDbTypeToCSharpBase(dbType));
    }

    private static List<ParameterMetadata> CreateUnknownParameters(PostgreSqlParameterLayout layout) {
        var parameters = new List<ParameterMetadata>(layout.Names.Count);
        foreach (string name in layout.Names)
            parameters.Add(new ParameterMetadata(
                name,
                null,
                true,
                0,
                ParameterDirection.Input,
                0,
                0,
                "object",
                binding: layout.IsPositional ? ParameterBinding.Positional : ParameterBinding.Named));
        return parameters;
    }

    private static async Task<List<ParameterMetadata>> DiscoverStoredProcedureParametersAsync(
        NpgsqlConnection cnn,
        string procedureName,
        CancellationToken ct) {

        await using var command = new NpgsqlCommand("""
WITH matches AS (
    SELECT
        p.oid,
        p.proargnames,
        COALESCE(p.proallargtypes, p.proargtypes::oid[]) AS all_types,
        p.proargmodes
    FROM pg_proc AS p
    INNER JOIN pg_namespace AS n ON n.oid = p.pronamespace
    WHERE p.prokind = 'p'
      AND (
          (strpos(@name, '.') > 0 AND n.nspname || '.' || p.proname = @name)
          OR
          (strpos(@name, '.') = 0 AND p.proname = @name)
      )
)
SELECT
    m.oid,
    a.ordinality,
    COALESCE(m.proargnames[a.ordinality], 'parameter' || a.ordinality::text) AS parameter_name,
    format_type(a.type_oid, NULL) AS type_name,
    COALESCE(m.proargmodes[a.ordinality], 'i'::"char")::text AS parameter_mode
FROM matches AS m
LEFT JOIN LATERAL unnest(m.all_types) WITH ORDINALITY AS a(type_oid, ordinality) ON TRUE
ORDER BY m.oid, a.ordinality;
""", cnn);
        command.Parameters.AddWithValue("name", procedureName);

        var parameters = new List<ParameterMetadata>();
        uint? procedureOid = null;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            uint currentOid = Convert.ToUInt32(reader.GetValue(0));
            if (procedureOid is null)
                procedureOid = currentOid;
            else if (procedureOid.Value != currentOid)
                throw new InvalidOperationException($"PostgreSQL procedure '{procedureName}' is overloaded. Use an explicit SQL CALL query so the intended overload is unambiguous.");

            if (reader.IsDBNull(1))
                continue;

            string name = reader.GetString(2);
            string typeName = reader.GetString(3);
            string mode = reader.GetString(4);
            ProviderTypeInfo type = PostgreSqlTypeParser.TryParse(typeName, out ProviderTypeInfo parsed)
                ? parsed
                : new ProviderTypeInfo(
                    null,
                    "object",
                    ProviderType: new ProviderParameterType(
                        DatabaseType.PostgreSql,
                        TypeDeclarationParser.Parse(typeName).Name.Trim()));

            parameters.Add(new ParameterMetadata(
                name,
                type.DbType,
                true,
                type.Size,
                mode switch {
                    "o" => ParameterDirection.Output,
                    "b" => ParameterDirection.InputOutput,
                    _ => ParameterDirection.Input
                },
                type.Precision,
                type.Scale,
                type.CSharpType,
                type.ProviderType));
        }

        if (procedureOid is null)
            throw new InvalidOperationException($"PostgreSQL procedure '{procedureName}' was not found.");

        return parameters;
    }

    private static async Task<List<ParameterMetadata>> DiscoverTextColumnsAsync(
        NpgsqlConnection cnn,
        string sql,
        List<ParameterMetadata> parameters,
        CancellationToken ct) {

        await using var command = new NpgsqlCommand(sql, cnn);
        foreach (ParameterMetadata metadata in parameters) {
            var parameter = new NpgsqlParameter { ParameterName = metadata.Binding == ParameterBinding.Positional ? string.Empty : metadata.DbName, Value = DBNull.Value };
            if (metadata.DbType is { } dbType)
                parameter.DbType = dbType;
            if (metadata.ProviderType is { Database: DatabaseType.PostgreSql } providerType)
                parameter.DataTypeName = providerType.DataTypeName;
            if (metadata.Size != 0)
                parameter.Size = metadata.Size;
            if (metadata.Precision != 0)
                parameter.Precision = metadata.Precision;
            if (metadata.Scale != 0)
                parameter.Scale = metadata.Scale;
            command.Parameters.Add(parameter);
        }

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
        var schema = reader.GetColumnSchema();
        var columns = new List<ParameterMetadata>(schema.Count);

        for (int i = 0; i < schema.Count; i++) {
            DbColumn column = schema[i];
            string typeName = reader.GetDataTypeName(i);
            PostgreSqlTypeParser.TryParse(typeName, out ProviderTypeInfo parsed);
            string csharpType = CSharpTypeNames.FromType(column.DataType ?? reader.GetFieldType(i));
            if (csharpType == "object" && parsed.CSharpType != "object")
                csharpType = parsed.CSharpType;

            columns.Add(new ParameterMetadata(
                column.ColumnName ?? reader.GetName(i),
                parsed.DbType,
                column.AllowDBNull ?? true,
                column.ColumnSize ?? parsed.Size,
                ParameterDirection.Input,
                ToByte(column.NumericPrecision) != 0 ? ToByte(column.NumericPrecision) : parsed.Precision,
                ToByte(column.NumericScale) != 0 ? ToByte(column.NumericScale) : parsed.Scale,
                csharpType,
                parsed.ProviderType));
        }

        return columns;
    }

    private static byte ToByte(int? value) =>
        value is > 0 and <= byte.MaxValue ? (byte)value.Value : (byte)0;
}
