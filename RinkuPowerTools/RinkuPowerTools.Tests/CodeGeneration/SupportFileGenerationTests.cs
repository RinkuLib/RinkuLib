using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class SupportFileGenerationTests
{
    [Fact]
    public async Task SupportFile_ContainsPowerToolsSqlCacheAndExistingExtensions()
    {
        using var temp = new TempDirectory();
        var result = await GenerateAsync(temp.Path);

        Assert.Contains("class RinkuPowerTools", result.SupportCode);
        Assert.Contains("ConcurrentDictionary<string, string> SqlFiles", result.SupportCode);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", result.SupportCode);
        Assert.Contains("string GetSqlFile(string path)", result.SupportCode);
        Assert.Contains("SqlFiles.GetOrAdd", result.SupportCode);
        Assert.Contains("Path.IsPathRooted(path)", result.SupportCode);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, path)", result.SupportCode);
        Assert.Contains("DbParameter Add(this DbCommand command", result.SupportCode);
        Assert.DoesNotContain("class SqlFileCache", result.SupportCode);
        Assert.DoesNotContain("RinkuDbCommandExtensions", result.SupportCode);
    }

    [Fact]
    public async Task SupportFile_IsRegeneratedWhenItAlreadyExists()
    {
        using var temp = new TempDirectory();
        string supportPath = System.IO.Path.Combine(temp.Path, ".PowerTools.rinku.cs");
        await File.WriteAllTextAsync(
            supportPath,
            "stale support file",
            TestContext.Current.CancellationToken);

        var result = await GenerateAsync(temp.Path);

        Assert.NotEqual("stale support file", result.SupportCode);
        Assert.Contains("class RinkuPowerTools", result.SupportCode);
    }

    private static Task<(string CommandPath, string CommandCode, string SupportCode)> GenerateAsync(string projectDirectory)
    {
        var query = new QuerySetting
        {
            MethodName = "GetAlbums",
            Target = "SELECT 1",
            SourceType = QuerySourceType.Text
        };

        return GeneratorTestHelper.GenerateAsync(
            projectDirectory,
            query,
            new DiscoveredSchema("SELECT 1", [], []));
    }
}
