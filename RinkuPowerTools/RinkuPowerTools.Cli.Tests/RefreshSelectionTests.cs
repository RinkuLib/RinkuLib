namespace RinkuPowerTools.Cli.Tests;

public class RefreshSelectionTests {
    [Fact]
    public async Task RefreshAllWithNoConfigsIsNoOp() {
        using var project = new TestProject();
        int calls = 0;

        int exitCode = await RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh"]), (_, _, _) => { calls++; return Task.FromResult(string.Empty); }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RefreshAllRefreshesDefaultAndEveryNamedConfig() {
        using var project = new TestProject();
        await ConfigStore.SaveAsync(project.Context, null, new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Default.cs" }, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "dev", new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Dev.cs" }, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "test.local", new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Test.cs" }, CancellationToken.None);
        File.WriteAllText(Path.Combine(project.DirectoryPath, "rinkuptOther.json"), "{}");

        List<string> outputs = [];
        int exitCode = await RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh"]), (settings, _, _) => { outputs.Add(settings.OutputPath); return Task.FromResult(settings.OutputPath); }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(3, outputs.Count);
        Assert.Contains("Default.cs", outputs);
        Assert.Contains("Dev.cs", outputs);
        Assert.Contains("Test.cs", outputs);
    }

    [Fact]
    public async Task RefreshNameRefreshesOnlyNamedConfig() {
        using var project = new TestProject();
        await ConfigStore.SaveAsync(project.Context, null, new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Default.cs" }, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "dev", new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Dev.cs" }, CancellationToken.None);

        List<string> outputs = [];
        int exitCode = await RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh", "dev"]), (settings, _, _) => { outputs.Add(settings.OutputPath); return Task.FromResult(settings.OutputPath); }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["Dev.cs"], outputs);
    }

    [Fact]
    public async Task RefreshDefaultRefreshesOnlyUnnamedConfig() {
        using var project = new TestProject();
        await ConfigStore.SaveAsync(project.Context, null, new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Default.cs" }, CancellationToken.None);
        await ConfigStore.SaveAsync(project.Context, "dev", new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Dev.cs" }, CancellationToken.None);

        List<string> outputs = [];
        int exitCode = await RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh", "--default"]), (settings, _, _) => { outputs.Add(settings.OutputPath); return Task.FromResult(settings.OutputPath); }, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["Default.cs"], outputs);
    }

    [Fact]
    public async Task ExplicitMissingNamedConfigFails() {
        using var project = new TestProject();
        CliException error = await Assert.ThrowsAsync<CliException>(() => RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh", "dev"]), (_, _, _) => Task.FromResult(string.Empty), CancellationToken.None));
        Assert.Equal(2, error.ExitCode);
    }

    [Fact]
    public async Task ExplicitMissingDefaultConfigFails() {
        using var project = new TestProject();
        CliException error = await Assert.ThrowsAsync<CliException>(() => RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh", "--default"]), (_, _, _) => Task.FromResult(string.Empty), CancellationToken.None));
        Assert.Equal(2, error.ExitCode);
    }

    [Fact]
    public async Task NameAndDefaultCannotBeCombined() {
        using var project = new TestProject();
        CliException error = await Assert.ThrowsAsync<CliException>(() => RefreshService.RunAsync(project.Context, CliArguments.Parse(["refresh", "dev", "--default"]), (_, _, _) => Task.FromResult(string.Empty), CancellationToken.None));
        Assert.Equal(2, error.ExitCode);
    }
}
