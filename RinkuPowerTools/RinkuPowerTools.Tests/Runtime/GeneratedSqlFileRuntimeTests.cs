using System.Collections.Concurrent;
using System.Reflection;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Runtime;

public class GeneratedSqlFileRuntimeTests
{
    [Fact]
    public async Task PublicDictionary_IsCaseInsensitive()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        runtime.SqlFiles["Sql/GetAlbums.sql"] = "SELECT 1";

        string sql = runtime.GetSqlFile("sql/getalbums.SQL");

        Assert.Equal("SELECT 1", sql);
        Assert.Single(runtime.SqlFiles);
    }

    [Fact]
    public async Task ExistingDictionaryValue_AvoidsFileRead()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        const string missingPath = "This/File/Does/Not/Exist.sql";
        runtime.SqlFiles[missingPath] = "SELECT 1";

        string sql = runtime.GetSqlFile(missingPath);

        Assert.Equal("SELECT 1", sql);
    }

    [Fact]
    public async Task RelativePath_LoadsFromAppBaseDirectoryAndCaches()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        string relativeDirectory = System.IO.Path.Combine("RinkuPowerToolsTests", Guid.NewGuid().ToString("N"));
        string relativePath = System.IO.Path.Combine(relativeDirectory, "Query.sql");
        string fullDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        string fullPath = System.IO.Path.Combine(AppContext.BaseDirectory, relativePath);
        Directory.CreateDirectory(fullDirectory);

        try
        {
            await File.WriteAllTextAsync(fullPath, "SELECT 1", TestContext.Current.CancellationToken);
            Assert.Equal("SELECT 1", runtime.GetSqlFile(relativePath));

            await File.WriteAllTextAsync(fullPath, "SELECT 2", TestContext.Current.CancellationToken);
            Assert.Equal("SELECT 1", runtime.GetSqlFile(relativePath));
            Assert.Equal("SELECT 1", runtime.SqlFiles[relativePath]);
        }
        finally
        {
            Directory.Delete(fullDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AbsolutePath_LoadsAndCaches()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "Query.sql");
        await File.WriteAllTextAsync(path, "SELECT 1", TestContext.Current.CancellationToken);

        Assert.Equal("SELECT 1", runtime.GetSqlFile(path));

        await File.WriteAllTextAsync(path, "SELECT 2", TestContext.Current.CancellationToken);
        Assert.Equal("SELECT 1", runtime.GetSqlFile(path));
    }

    [Fact]
    public async Task RemovingEntry_MakesNextCallReloadFile()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "Query.sql");
        await File.WriteAllTextAsync(path, "SELECT 1", TestContext.Current.CancellationToken);
        Assert.Equal("SELECT 1", runtime.GetSqlFile(path));

        await File.WriteAllTextAsync(path, "SELECT 2", TestContext.Current.CancellationToken);
        Assert.True(runtime.SqlFiles.TryRemove(path, out _));

        Assert.Equal("SELECT 2", runtime.GetSqlFile(path));
    }

    [Fact]
    public async Task AssigningEntry_OverridesLaterCalls()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "Query.sql");
        await File.WriteAllTextAsync(path, "SELECT file", TestContext.Current.CancellationToken);

        runtime.SqlFiles[path] = "SELECT override";

        Assert.Equal("SELECT override", runtime.GetSqlFile(path));
    }

    [Fact]
    public async Task ConcurrentFirstAccess_ReturnsOneCachedValue()
    {
        GeneratedRuntime runtime = await CreateRuntimeAsync();
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "Query.sql");
        await File.WriteAllTextAsync(path, "SELECT 1", TestContext.Current.CancellationToken);

        var tasks = new Task<string>[32];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = Task.Run(() => runtime.GetSqlFile(path));
        string[] results = await Task.WhenAll(tasks);

        for (int i = 0; i < results.Length; i++)
            Assert.Equal("SELECT 1", results[i]);
        Assert.Single(runtime.SqlFiles);
    }

    private static async Task<GeneratedRuntime> CreateRuntimeAsync()
    {
        using var temp = new TempDirectory();
        var generated = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting
            {
                MethodName = "GetAlbums",
                Target = "SELECT 1",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT 1", [], []));

        Assembly assembly = GeneratedAssemblyCompiler.Compile(generated.SupportCode);
        Type type = assembly.GetType("TestApp.RinkuPowerTools")
            ?? throw new InvalidOperationException("Generated TestApp.RinkuPowerTools type was not found.");
        FieldInfo field = type.GetField("SqlFiles", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Generated RinkuPowerTools.SqlFiles field was not found.");
        MethodInfo method = type.GetMethod("GetSqlFile", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Generated RinkuPowerTools.GetSqlFile method was not found.");
        object? value = field.GetValue(null);
        if (value is not ConcurrentDictionary<string, string> sqlFiles)
            throw new InvalidOperationException("Generated RinkuPowerTools.SqlFiles had an unexpected value.");

        return new GeneratedRuntime(sqlFiles, method);
    }

    private sealed class GeneratedRuntime(
        ConcurrentDictionary<string, string> sqlFiles,
        MethodInfo getSqlFile)
    {
        public ConcurrentDictionary<string, string> SqlFiles { get; } = sqlFiles;

        public string GetSqlFile(string path) =>
            (string)(getSqlFile.Invoke(null, [path])
                ?? throw new InvalidOperationException("GetSqlFile returned null."));
    }
}
