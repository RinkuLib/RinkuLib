namespace RinkuPowerTools.Cli.Tests;

public class CliConfigTests {
    [Fact]
    public async Task Hook_generation_can_be_enabled_and_disabled_per_query()
    {
        using var project = new TestProject();
        CancellationToken ct = TestContext.Current.CancellationToken;
        await Cli.RunAsync(["config", "init", "--project", project.ProjectFile, "--connection-target", "test"], ct);
        await Cli.RunAsync(["query", "add", "Load", "--project", project.ProjectFile, "--sql", "SELECT 1", "--generate-hooks", "true"], ct);
        Assert.True(Assert.Single((await ConfigStore.LoadAsync(project.Context, null, ct)).Queries).GenerateHooks);
        await Cli.RunAsync(["query", "set", "Load", "--project", project.ProjectFile, "--generate-hooks", "false"], ct);
        Assert.False(Assert.Single((await ConfigStore.LoadAsync(project.Context, null, ct)).Queries).GenerateHooks);
    }

    [Fact]
    public async Task DefaultOnlyCorrectionCanBeSetAndClearedWithoutInventingMetadata() {
        using var project = new TestProject();
        CancellationToken ct = TestContext.Current.CancellationToken;
        await Cli.RunAsync(["config", "init", "--project", project.ProjectFile, "--connection-target", "test"], ct);
        await Cli.RunAsync(["query", "add", "Load", "--project", project.ProjectFile, "--sql", "SELECT @Id"], ct);
        await Cli.RunAsync(["query", "param", "Load", "@Id", "--project", project.ProjectFile, "--default", "null"], ct);
        var parameter = Assert.Single(Assert.Single((await ConfigStore.LoadAsync(project.Context, null, ct)).Queries).Parameters);
        Assert.Equal("null", parameter.DefaultValue);
        Assert.Null(parameter.Type);
        Assert.Null(parameter.IsNullable);
        await Cli.RunAsync(["query", "param", "Load", "@Id", "--project", project.ProjectFile, "--default="], ct);
        parameter = Assert.Single(Assert.Single((await ConfigStore.LoadAsync(project.Context, null, ct)).Queries).Parameters);
        Assert.Null(parameter.DefaultValue);
    }

    [Fact]
    public async Task Constants_source_can_be_set_switched_and_removed_without_losing_queries() {
        using var project = new TestProject();
        await Cli.RunAsync(["config", "init", "--project", project.ProjectFile, "--connection-target", "Data Source=:memory:", "--constants-path", "Constants.sql"], CancellationToken.None);
        await Cli.RunAsync(["query", "add", "Existing", "--project", project.ProjectFile, "--sql", "SELECT 1"], CancellationToken.None);
        Assert.Equal(QuerySourceType.FromFile, (await ConfigStore.LoadAsync(project.Context, null, CancellationToken.None)).Constants!.ToQuery().SourceType);
        await Cli.RunAsync(["config", "set", "--project", project.ProjectFile, "--constants-proc", "dbo.Constants"], CancellationToken.None);
        Assert.Equal(QuerySourceType.StoredProcedure, (await ConfigStore.LoadAsync(project.Context, null, CancellationToken.None)).Constants!.ToQuery().SourceType);
        await Cli.RunAsync(["config", "set", "--project", project.ProjectFile, "--constants-sql", "SELECT 'Name'"], CancellationToken.None);
        Assert.Equal(QuerySourceType.Text, (await ConfigStore.LoadAsync(project.Context, null, CancellationToken.None)).Constants!.ToQuery().SourceType);
        await Assert.ThrowsAsync<CliException>(() => Cli.RunAsync(["config", "set", "--project", project.ProjectFile, "--constants-path", "Constants.sql", "--constants-none"], CancellationToken.None));
        await Cli.RunAsync(["config", "set", "--project", project.ProjectFile, "--constants-none"], CancellationToken.None);
        var loaded = await ConfigStore.LoadAsync(project.Context, null, CancellationToken.None);
        Assert.Null(loaded.Constants); Assert.Single(loaded.Queries);
    }
    [Fact]
    public async Task CliCanEditAConfigCreatedByTheExtensionModel() {
        using var project = new TestProject();
        ProjectContext context = project.Context;
        var extensionSettings = new ExtensionSettings {
            OutputPath = "Generated/DbCommands.cs",
            Namespace = "TestApp.Generated",
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "original"
        };
        extensionSettings.Queries.Add(new QuerySetting { MethodName = "Existing", SourceType = QuerySourceType.Text, Target = "SELECT 1" });
        await ConfigStore.SaveAsync(context, null, extensionSettings, CancellationToken.None);

        int exitCode = await Cli.RunAsync(["query", "add", "AddedByCli", "--project", project.ProjectFile, "--sql", "SELECT 2 AS Value", "--result", "AddedRow"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        ExtensionSettings loaded = await ConfigStore.LoadAsync(context, null, CancellationToken.None);
        Assert.Equal("Generated/DbCommands.cs", loaded.OutputPath);
        Assert.Equal("TestApp.Generated", loaded.Namespace);
        Assert.Equal("original", loaded.ConnectionTarget);
        Assert.Equal(2, loaded.Queries.Count);
        Assert.Contains(loaded.Queries, x => x.MethodName == "Existing" && x.Target == "SELECT 1");
        Assert.Contains(loaded.Queries, x => x.MethodName == "AddedByCli" && x.Target == "SELECT 2 AS Value" && x.ResultSetName == "AddedRow");
    }

    [Fact]
    public async Task CliCanModifyNamedConfigWithoutTouchingDefaultConfig() {
        using var project = new TestProject();
        ProjectContext context = project.Context;
        await ConfigStore.SaveAsync(context, null, new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Default.cs" }, CancellationToken.None);
        await ConfigStore.SaveAsync(context, "dev", new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "test", OutputPath = "Dev.cs" }, CancellationToken.None);

        int exitCode = await Cli.RunAsync(["config", "set", "--project", project.ProjectFile, "--config", "dev", "--output", "Dev2.cs"], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal("Default.cs", (await ConfigStore.LoadAsync(context, null, CancellationToken.None)).OutputPath);
        Assert.Equal("Dev2.cs", (await ConfigStore.LoadAsync(context, "dev", CancellationToken.None)).OutputPath);
    }
}
