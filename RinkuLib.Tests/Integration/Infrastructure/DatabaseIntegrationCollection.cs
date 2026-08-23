using Xunit;

namespace RinkuLib.Tests.TestContainers;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseIntegrationCollection {
    public const string Name = "Database integrations";
}
