using Microsoft.Data.SqlClient;

namespace RinkuPowerTools.Tests.Infrastructure;

internal static class GeneratorTestHelper
{
    public static async Task<(string CommandPath, string CommandCode, string SupportCode)> GenerateAsync(
        string projectDirectory,
        QuerySetting query,
        DiscoveredSchema schema,
        string baseNamespace = "TestApp")
    {
        var settings = new ExtensionSettings
        {
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "unused",
            OutputPath = "Generated",
            ClassName = "DbCommands",
            Queries = [query]
        };
        settings.SetProjectDirectory(projectDirectory);

        using var connection = new SqlConnection();
        string commandPath = await MainClassGenerator.GenerateClassAsync(
            new StaticSchemaDiscoverer(schema),
            connection,
            settings,
            baseNamespace,
            CancellationToken.None);

        string commandCode = await File.ReadAllTextAsync(commandPath);
        string supportCode = await File.ReadAllTextAsync(System.IO.Path.Combine(projectDirectory, ".PowerTools.rinku.cs"));
        return (commandPath, commandCode, supportCode);
    }
}
