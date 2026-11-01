using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class SupportFileGenerationTests
{
    [Fact]
    public async Task SupportFile_ContainsPowerToolsSqlCacheAndExistingExtensions()
    {
        using var temp = new TempDirectory();
        var (_, _, supportCode) = await GenerateAsync(temp.Path);

        Assert.Contains("class RinkuPowerTools", supportCode);
        Assert.Contains("ConcurrentDictionary<string, string> SqlFiles", supportCode);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", supportCode);
        Assert.Contains("string GetSqlFile(string path)", supportCode);
        Assert.Contains("SqlFiles.GetOrAdd", supportCode);
        Assert.Contains("Path.IsPathRooted(path)", supportCode);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, path)", supportCode);
        Assert.Contains("DbParameter Add(this DbCommand command", supportCode);
        Assert.Contains("namespace RinkuPowerTools.Generated", supportCode);
        Assert.Contains("class SchemaAttribute", supportCode);
        Assert.Contains("DateTime? LastUpdated", supportCode);
        Assert.DoesNotContain("class BasedOnAttribute", supportCode);
        Assert.DoesNotContain("Last reload:", supportCode);
        Assert.DoesNotContain("class SqlFileCache", supportCode);
        Assert.DoesNotContain("RinkuDbCommandExtensions", supportCode);
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

        var (_, _, supportCode) = await GenerateAsync(temp.Path);

        Assert.NotEqual("stale support file", supportCode);
        Assert.Contains("class RinkuPowerTools", supportCode);
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
