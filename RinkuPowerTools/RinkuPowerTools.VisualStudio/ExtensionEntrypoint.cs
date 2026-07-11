using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace RinkuPowerTools.VisualStudio {
    /// <summary>
    /// Extension entrypoint for the VisualStudio.Extensibility extension.
    /// </summary>
    [VisualStudioContribution]
    internal class ExtensionEntrypoint : Extension {
        /// <inheritdoc/>
        public override ExtensionConfiguration ExtensionConfiguration => new() {
            Metadata = new(
                    id: "RinkuPowerTools.VisualStudio.facb6f8c-8756-4877-b9b7-2969c4308b62",
                    version: this.ExtensionAssemblyVersion,
                    publisherName: "Rinku",
                    displayName: "RinkuPowerTools",
                    description: "Code generator to help developement with the Rinku nuget"),
        };

        /// <inheritdoc />
        protected override void InitializeServices(IServiceCollection services) {
            base.InitializeServices(services);
        }
    }
}