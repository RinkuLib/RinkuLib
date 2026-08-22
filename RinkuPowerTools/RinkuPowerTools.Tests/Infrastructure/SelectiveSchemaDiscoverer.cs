using System.Data.Common;

namespace RinkuPowerTools.Tests.Infrastructure;

internal sealed class SelectiveSchemaDiscoverer(
    Func<QuerySetting, DiscoveredSchema> discover) : SchemaDiscoverer
{
    public override Task<DiscoveredSchema> DiscoverSchemaAsync(
        ExtensionSettings settings,
        DbConnection cnn,
        QuerySetting query,
        CancellationToken ct) => Task.FromResult(discover(query));
}
