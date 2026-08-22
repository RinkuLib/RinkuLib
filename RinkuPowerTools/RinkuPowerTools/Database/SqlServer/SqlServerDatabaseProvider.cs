using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace RinkuPowerTools;

public sealed class SqlServerDatabaseProvider : DatabaseProvider {
    public static readonly SqlServerDatabaseProvider Instance = new();
    private static readonly string[] TypeSuggestions = [
        "bigint", "binary", "bit", "char", "date", "datetime", "datetime2",
        "datetimeoffset", "decimal", "float", "image", "int", "money", "nchar",
        "ntext", "numeric", "nvarchar", "real", "smalldatetime", "smallint",
        "smallmoney", "text", "time", "timestamp", "tinyint", "uniqueidentifier",
        "varbinary", "varchar", "xml"
    ];
    private static readonly SqlServerSchemaDiscoverer Discoverer = new();

    private SqlServerDatabaseProvider() { }

    public override DatabaseType Type => DatabaseType.SqlServer;
    public override string DisplayName => "SQL Server";
    public override DatabaseCapabilities Capabilities => DatabaseCapabilities.StoredProcedures | DatabaseCapabilities.OutputParameters;
    public override SchemaDiscoverer SchemaDiscoverer => Discoverer;
    public override IReadOnlyList<string> ParameterTypeSuggestions => TypeSuggestions;
    public override string DefaultParameterType => "nvarchar";
    public override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public override int GetConnectionStringScore(string connectionString, ConnectionStringInfo parsed) {
        try {
            _ = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException) {
            return 0;
        }

        int score = 0;
        if (parsed.HasAny(
            "Initial Catalog", "Integrated Security", "Trusted_Connection", "AttachDbFilename",
            "MultipleActiveResultSets", "MultiSubnetFailover", "ApplicationIntent", "Packet Size"))
            score += 100;
        if (parsed.HasAny("Server", "Data Source", "Address", "Addr", "Network Address"))
            score += 20;
        return score;
    }

    public override Task<IReadOnlyList<string>> GetStoredProceduresAsync(DbConnection connection, CancellationToken ct) =>
        SqlServerSchemaDiscoverer.GetStoredProceduresAsync((SqlConnection)connection, ct);
}
