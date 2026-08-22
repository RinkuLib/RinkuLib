using System.Data.Common;

namespace RinkuPowerTools.Tests.Infrastructure;

internal sealed class StaticSchemaDiscoverer(DiscoveredSchema schema) : SchemaDiscoverer
{
    public override Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct) => Task.FromResult(schema);
}
