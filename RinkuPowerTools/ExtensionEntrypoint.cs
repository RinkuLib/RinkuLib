using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace RinkuPowerTools; 
/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension {
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new() {
        Metadata = new(
                id: "RinkuPowerTools.42484c81-be2a-422d-a268-6fda58f6b095",
                version: this.ExtensionAssemblyVersion,
                publisherName: "Publisher name",
                displayName: "RinkuPowerTools",
                description: "Extension description"),
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection) {
        base.InitializeServices(serviceCollection);

        // You can configure dependency injection here by adding services to the serviceCollection.
    }
}
