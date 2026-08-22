using System.Data;
using System.Data.Common;

namespace RinkuPowerTools;

public abstract class SchemaDiscoverer {
    public abstract Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct);

    protected static async Task EnsureOpenAsync(DbConnection cnn, CancellationToken ct) {
        if (cnn.State != ConnectionState.Open)
            await cnn.OpenAsync(ct);
    }

    protected static List<ParameterMetadata> ApplyUserOverrides(
        List<ParameterMetadata> discoveredParams,
        List<ParameterOverride> userOverrides,
        Func<string, ProviderTypeInfo> parseType) {

        if (userOverrides.Count == 0)
            return discoveredParams;

        foreach (ParameterOverride overrideParam in userOverrides) {
            ParameterMetadata? target = null;
            foreach (ParameterMetadata parameter in discoveredParams) {
                if (ParameterNamesEqual(parameter.DbName, overrideParam.Name)) {
                    target = parameter;
                    break;
                }
            }

            if (target is null)
                throw new InvalidOperationException($"Configuration error: Defined parameter override '{overrideParam.Name}' does not exist in the database query target.");

            if (!string.IsNullOrWhiteSpace(overrideParam.Type))
                target.UpdateType(parseType(overrideParam.Type), overrideParam.IsNullable);
            else if (overrideParam.IsNullable is { } nullable)
                target.UpdateNullability(nullable);
        }

        return discoveredParams;
    }

    private static bool ParameterNamesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            left.TrimStart('@', '$', ':'),
            right.TrimStart('@', '$', ':'),
            StringComparison.OrdinalIgnoreCase);
}

public record DiscoveredSchema(
    string SQL,
    List<ParameterMetadata> Parameters,
    List<ParameterMetadata> ResultColumns);
