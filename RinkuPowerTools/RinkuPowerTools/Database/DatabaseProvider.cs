using System.Data.Common;
using System.Text;

namespace RinkuPowerTools;

public enum DatabaseType : byte {
    SqlServer,
    PostgreSql,
    Sqlite
}

[Flags]
public enum DatabaseCapabilities : byte {
    None = 0,
    StoredProcedures = 1,
    OutputParameters = 2
}

public abstract class DatabaseProvider {
    public abstract DatabaseType Type { get; }
    public abstract string DisplayName { get; }
    public abstract DatabaseCapabilities Capabilities { get; }
    public abstract SchemaDiscoverer SchemaDiscoverer { get; }
    public abstract IReadOnlyList<string> ParameterTypeSuggestions { get; }
    public abstract string DefaultParameterType { get; }
    public abstract DbConnection CreateConnection(string connectionString);
    public abstract int GetConnectionStringScore(string connectionString, ConnectionStringInfo parsed);

    public virtual Task<IReadOnlyList<string>> GetStoredProceduresAsync(DbConnection connection, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    internal virtual void AppendGeneratedParameterType(
        StringBuilder sb,
        string parameterVariable,
        ProviderParameterType providerType) =>
        throw new NotSupportedException($"{DisplayName} does not define generated provider-specific parameter typing.");

    public bool Supports(DatabaseCapabilities capability) =>
        (Capabilities & capability) == capability;
}

public sealed class ConnectionStringInfo {
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public ConnectionStringInfo(string connectionString) {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (string key in builder.Keys)
            _values[key] = Convert.ToString(builder[key]) ?? string.Empty;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public bool HasAny(params string[] keys) {
        foreach (string key in keys)
            if (_values.ContainsKey(key))
                return true;
        return false;
    }

    public string? Get(string key) =>
        _values.TryGetValue(key, out string? value) ? value : null;
}

public static class DatabaseProviders {
    private static readonly DatabaseProvider[] Providers = [
        SqlServerDatabaseProvider.Instance,
        PostgreSqlDatabaseProvider.Instance,
        SqliteDatabaseProvider.Instance
    ];

    public static IReadOnlyList<DatabaseProvider> All => Providers;

    public static DatabaseProvider Get(DatabaseType type) => type switch {
        DatabaseType.SqlServer => SqlServerDatabaseProvider.Instance,
        DatabaseType.PostgreSql => PostgreSqlDatabaseProvider.Instance,
        DatabaseType.Sqlite => SqliteDatabaseProvider.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static bool TryParseType(string? value, out DatabaseType type) {
        if (Enum.TryParse(value, true, out type))
            return true;
        if (string.Equals(value, "Postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "PostgreSQL", StringComparison.OrdinalIgnoreCase)) {
            type = DatabaseType.PostgreSql;
            return true;
        }
        type = default;
        return false;
    }

    public static DatabaseProvider Resolve(string connectionString, DatabaseType? configuredType = null) {
        if (configuredType is { } type)
            return Get(type);

        var parsed = new ConnectionStringInfo(connectionString);
        DatabaseProvider? best = null;
        int bestScore = 0;
        int secondScore = 0;

        foreach (DatabaseProvider provider in Providers) {
            int score = provider.GetConnectionStringScore(connectionString, parsed);
            if (score > bestScore) {
                secondScore = bestScore;
                bestScore = score;
                best = provider;
            }
            else if (score > secondScore) {
                secondScore = score;
            }
        }

        if (best is null || bestScore == 0)
            throw new InvalidOperationException("The database type could not be inferred from the connection string. Set the Database option explicitly.");

        if (bestScore == secondScore)
            throw new InvalidOperationException("The database type is ambiguous for this connection string. Set the Database option explicitly.");

        return best;
    }
}
