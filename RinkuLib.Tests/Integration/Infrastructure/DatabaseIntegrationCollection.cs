using Xunit;

namespace RinkuLib.Tests.Integration.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseIntegrationCollection {
    public const string Name = "Database integrations";
}
