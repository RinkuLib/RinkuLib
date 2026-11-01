namespace RinkuPowerTools.Cli.Tests;

public class ConfigStoreTests {
    [Fact]
    public void ResolvesDefaultNamedAndExplicitConfigPaths() {
        using var project = new TestProject();
        ProjectContext context = project.Context;

        Assert.Equal(Path.Combine(project.DirectoryPath, "rinkupt.json"), ConfigStore.GetPath(context, null));
        Assert.Equal(Path.Combine(project.DirectoryPath, "rinkupt.dev.json"), ConfigStore.GetPath(context, "dev"));
        Assert.Equal(Path.Combine(project.DirectoryPath, "config", "custom.json"), ConfigStore.GetPath(context, Path.Combine("config", "custom.json")));
    }

    [Fact]
    public async Task SavesAndLoadsSharedExtensionSettings() {
        using var project = new TestProject();
        var settings = new ExtensionSettings {
            OutputPath = "Generated/DbCommands.cs",
            Namespace = "TestApp.Generated",
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "Server=(local);Database=Test;Trusted_Connection=True;",
            IsInternal = true
        };
        settings.Queries.Add(new QuerySetting { MethodName = "GetAlbums", SourceType = QuerySourceType.Text, Target = "SELECT 1 AS Id", ResultSetName = "AlbumRow" });

        await ConfigStore.SaveAsync(project.Context, null, settings, CancellationToken.None);
        ExtensionSettings loaded = await ConfigStore.LoadAsync(project.Context, null, CancellationToken.None);

        Assert.Equal(settings.OutputPath, loaded.OutputPath);
        Assert.Equal(settings.Namespace, loaded.Namespace);
        Assert.Equal(settings.ConnectionSourceType, loaded.ConnectionSourceType);
        Assert.Equal(settings.ConnectionTarget, loaded.ConnectionTarget);
        Assert.True(loaded.IsInternal);
        QuerySetting query = Assert.Single(loaded.Queries);
        Assert.Equal("GetAlbums", query.MethodName);
        Assert.Equal("SELECT 1 AS Id", query.Target);
        Assert.Equal("AlbumRow", query.ResultSetName);
    }

    [Fact]
    public async Task SaveReplacesConfigWithoutLeavingTemporaryFiles() {
        using var project = new TestProject();
        ProjectContext context = project.Context;
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "One.cs" };

        await ConfigStore.SaveAsync(context, null, settings, CancellationToken.None);
        settings.OutputPath = "Two.cs";
        await ConfigStore.SaveAsync(context, null, settings, CancellationToken.None);

        ExtensionSettings loaded = await ConfigStore.LoadAsync(context, null, CancellationToken.None);
        Assert.Equal("Two.cs", loaded.OutputPath);
        Assert.Empty(Directory.EnumerateFiles(project.DirectoryPath, "rinkupt.json.*.tmp"));
    }

    [Fact]
    public async Task ListsDefaultAndNamedConfigs() {
        using var project = new TestProject();
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test" };

        await ConfigStore.SaveAsync(project.Context, null, settings, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "dev", settings, CancellationToken.None);

        string[] files = ConfigStore.List(project.Context).Select(Path.GetFileName).ToArray()!;
        Assert.Equal(["rinkupt.dev.json", "rinkupt.json"], files);
    }

    [Fact]
    public void GetsDefaultAndNamedConfigNames() {
        using var project = new TestProject();
        Assert.Null(ConfigStore.GetName(Path.Combine(project.DirectoryPath, "rinkupt.json")));
        Assert.Equal("dev", ConfigStore.GetName(Path.Combine(project.DirectoryPath, "rinkupt.dev.json")));
        Assert.Equal("dev.local", ConfigStore.GetName(Path.Combine(project.DirectoryPath, "rinkupt.dev.local.json")));
        Assert.Equal("ENUM", ConfigStore.GetName(Path.Combine(project.DirectoryPath, "rinku.ENUM.json")));
    }

    [Fact]
    public async Task NamedConstantsConfigIsDiscoveredAndUsesItsOwnOutputClassName() {
        using var project = new TestProject();
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test" };
        await ConfigStore.SaveAsync(project.Context, "rinku.ENUM.json", settings, CancellationToken.None);
        Assert.Equal(Path.Combine(project.DirectoryPath, "rinku.ENUM.json"), ConfigStore.GetPath(project.Context, "ENUM"));
        Assert.Equal("rinku.ENUM.json", Path.GetFileName(Assert.Single(ConfigStore.List(project.Context))));
        Assert.Equal("ENUM", (await ConfigStore.LoadAsync(project.Context, "ENUM", CancellationToken.None)).ClassName);
    }

    [Fact]
    public async Task ListIgnoresFilesThatOnlyShareThePrefix() {
        using var project = new TestProject();
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test" };

        await ConfigStore.SaveAsync(project.Context, null, settings, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "dev", settings, CancellationToken.None);
        File.WriteAllText(Path.Combine(project.DirectoryPath, "rinkuptOther.json"), "{}");

        string[] files = ConfigStore.List(project.Context).Select(Path.GetFileName).ToArray()!;
        Assert.Equal(["rinkupt.dev.json", "rinkupt.json"], files);
    }
}
