using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace RinkuPowerTools;

public sealed class SqliteDatabaseProvider : DatabaseProvider {
    public static readonly SqliteDatabaseProvider Instance = new();
    private static readonly string[] TypeSuggestions = [
        "integer", "real", "text", "blob", "numeric", "boolean", "date", "datetime", "guid"
    ];
    private static readonly SqliteSchemaDiscoverer Discoverer = new();

    private SqliteDatabaseProvider() { }

    public override DatabaseType Type => DatabaseType.Sqlite;
    public override string DisplayName => "SQLite";
    public override DatabaseCapabilities Capabilities => DatabaseCapabilities.None;
    public override SchemaDiscoverer SchemaDiscoverer => Discoverer;
    public override IReadOnlyList<string> ParameterTypeSuggestions => TypeSuggestions;
    public override string DefaultParameterType => "text";
    public override DbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);

    public override int GetConnectionStringScore(string connectionString, ConnectionStringInfo parsed) {
        try {
            _ = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException) {
            return 0;
        }

        int score = 0;
        if (parsed.HasAny("Mode", "Cache", "Foreign Keys", "Recursive Triggers", "Default Timeout", "Vfs"))
            score += 100;

        string? dataSource = parsed.Get("Data Source") ?? parsed.Get("DataSource") ?? parsed.Get("Filename");
        if (string.IsNullOrWhiteSpace(dataSource))
            return score;

        if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase) || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return score + 100;

        string extension = Path.GetExtension(dataSource);
        if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase))
            score += 80;

        return score + 20;
    }
}
