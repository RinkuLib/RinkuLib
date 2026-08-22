using System.Data.Common;
using System.Text;
using Npgsql;

namespace RinkuPowerTools;

public sealed class PostgreSqlDatabaseProvider : DatabaseProvider {
    public static readonly PostgreSqlDatabaseProvider Instance = new();
    private static readonly string[] TypeSuggestions = [
        "smallint", "integer", "bigint", "numeric", "real", "double precision",
        "boolean", "text", "varchar", "char", "date", "timestamp",
        "timestamp with time zone", "time", "interval", "uuid", "bytea", "json", "jsonb"
    ];
    private static readonly PostgreSqlSchemaDiscoverer Discoverer = new();

    private PostgreSqlDatabaseProvider() { }

    public override DatabaseType Type => DatabaseType.PostgreSql;
    public override string DisplayName => "PostgreSQL";
    public override DatabaseCapabilities Capabilities => DatabaseCapabilities.StoredProcedures | DatabaseCapabilities.OutputParameters;
    public override SchemaDiscoverer SchemaDiscoverer => Discoverer;
    public override IReadOnlyList<string> ParameterTypeSuggestions => TypeSuggestions;
    public override string DefaultParameterType => "text";
    public override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    public override int GetConnectionStringScore(string connectionString, ConnectionStringInfo parsed) {
        try {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException) {
            return 0;
        }

        int score = 0;
        if (parsed.Has("Host"))
            score += 100;
        if (parsed.HasAny("Username", "Search Path", "Include Error Detail", "SSL Mode", "Target Session Attributes"))
            score += 60;
        if (string.Equals(parsed.Get("Port"), "5432", StringComparison.Ordinal))
            score += 20;
        return score;
    }

    public override Task<IReadOnlyList<string>> GetStoredProceduresAsync(DbConnection connection, CancellationToken ct) =>
        PostgreSqlSchemaDiscoverer.GetStoredProceduresAsync((NpgsqlConnection)connection, ct);

    internal override void AppendGeneratedParameterType(
        StringBuilder sb,
        string parameterVariable,
        ProviderParameterType providerType) {

        if (providerType.Database != DatabaseType.PostgreSql)
            throw new ArgumentException("A PostgreSQL provider type is required.", nameof(providerType));

        string typedVariable = "npgsql_" + parameterVariable;
        string dataTypeName = providerType.DataTypeName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        sb.AppendLine($"        if ({parameterVariable} is not Npgsql.NpgsqlParameter {typedVariable})");
        sb.AppendLine("            throw new InvalidOperationException(\"PostgreSQL generated commands require Npgsql parameters.\");");
        sb.AppendLine($"        {typedVariable}.DataTypeName = \"{dataTypeName}\";");
    }
}
