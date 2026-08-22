using Microsoft.Data.SqlClient;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class FailureGenerationTests
{
    [Fact]
    public async Task OneFailedQuery_EmitsErrorWithoutRemovingSuccessfulQueries()
    {
        var settings = new ExtensionSettings
        {
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "unused",
            ClassName = "DbCommands",
            Queries =
            [
                new QuerySetting
                {
                    MethodName = "Good",
                    Target = "SELECT 1",
                    SourceType = QuerySourceType.Text
                },
                new QuerySetting
                {
                    MethodName = "Broken",
                    Target = "SELECT broken",
                    SourceType = QuerySourceType.Text
                }
            ]
        };
        var discoverer = new SelectiveSchemaDiscoverer(query =>
            query.MethodName == "Broken"
                ? throw new InvalidOperationException("Discovery failed")
                : new DiscoveredSchema(query.Target, [], []));
        using var connection = new SqlConnection();

        string code = await MainClassGenerator.GenerateClassCodeAsync(
            discoverer,
            null,
            connection,
            settings,
            "TestApp",
            CancellationToken.None);

        Assert.Contains("DbCommand Good(this DbConnection connection)", code);
        Assert.Contains("#error Query generation failed for method 'Broken'", code);
        Assert.Contains("Discovery failed", code);
    }
}
