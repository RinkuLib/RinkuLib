using System.Data;
using System.Data.Common;

namespace RinkuPowerTools.Core;

public abstract class SchemaDiscoverer {
    public abstract Task<DiscoveredSchema> DiscoverSchemaAsync(DbConnection cnn, QuerySetting query, List<ParameterOverride> userOverrides, string projectDirectory, CancellationToken ct);
}
public class SchemaDiscoverer<T> : SchemaDiscoverer where T : ISchemaDiscovererStrategy {
    public override async Task<DiscoveredSchema> DiscoverSchemaAsync(DbConnection cnn, QuerySetting query, List<ParameterOverride> userOverrides, string projectDirectory, CancellationToken ct) {
        if (cnn.State != ConnectionState.Open)
            await cnn.OpenAsync(ct);
        return await T.DiscoverSchemaAsync(cnn, query, userOverrides, projectDirectory, ct);
    }
}
public interface ISchemaDiscovererStrategy {
    abstract static Task<DiscoveredSchema> DiscoverSchemaAsync(DbConnection cnn, QuerySetting query, List<ParameterOverride> userOverrides, string projectDirectory, CancellationToken ct);
    public static List<ParameterMetadata> ApplyUserOverrides(List<ParameterMetadata> discoveredParams, List<ParameterOverride> userOverrides) {
        if (userOverrides == null || userOverrides.Count == 0)
            return discoveredParams;

        foreach (var overrideParam in userOverrides) {
            int targetIndex = discoveredParams.FindIndex(p =>
                string.Equals(p.DbName, overrideParam.Name, StringComparison.OrdinalIgnoreCase));

            if (targetIndex == -1)
                throw new InvalidOperationException($"Configuration error: Defined parameter override '{overrideParam.Name}' does not exist in the database query target.");

            discoveredParams[targetIndex].UpdateFromSqlType(overrideParam.Type, overrideParam.IsNullable);
        }

        return discoveredParams;
    }
}
public record DiscoveredSchema(string SQL, List<ParameterMetadata> Parameters, List<ParameterMetadata> ResultColumns);