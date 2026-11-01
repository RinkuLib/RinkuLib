namespace RinkuPowerTools.Cli.Tests;

public class RefreshIntegrationTests {
    [Fact]
    public async Task First_build_generates_and_compiles_constants_without_preexisting_output_items() {
        string targets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../RinkuPowerTools.Cli.Build/build/RinkuPowerTools.Cli.Build.targets"));
        string cli = typeof(Cli).Assembly.Location;
        using var project = new TestProject($"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><RinkuPowerToolsCliPath>{System.Security.SecurityElement.Escape(cli)}</RinkuPowerToolsCliPath></PropertyGroup><Import Project=\"{System.Security.SecurityElement.Escape(targets)}\" /></Project>");
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "Data Source=:memory:", Database = DatabaseType.Sqlite, Namespace = "TestApp", OutputPath = "Generated", Constants = new(QuerySourceType.Text, "SELECT 'Reader' AS Name, '-1' AS Value, 'long' AS Type, NULL AS Description, 'Roles' AS Parent") };
        var ct = TestContext.Current.CancellationToken;
        await ConfigStore.SaveAsync(project.Context, null, settings, ct);
        await File.WriteAllTextAsync(Path.Combine(project.DirectoryPath, "Consumer.cs"), "namespace TestApp; public class Consumer { public Roles Value => Roles.Reader; }", ct);
        for (int build = 0; build < 2; build++) {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet") { ArgumentList = { "build", project.ProjectFile, "--nologo" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct), stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            string log = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, log); Assert.DoesNotContain("warning CS2002", log);
        }
        Assert.Contains("enum Roles : long", await File.ReadAllTextAsync(Path.Combine(project.DirectoryPath, "Generated", "DbCommands.rinku.cs"), ct));
        Assert.True(File.Exists(Path.Combine(project.DirectoryPath, "bin", "Debug", "net8.0", "TestApp.dll")));
    }
    [Fact]
    public async Task RefreshGeneratesThroughSharedPowerToolsImplementationWhenConnectionIsProvided() {
        string? connectionString = Environment.GetEnvironmentVariable("RINKU_POWERTOOLS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        using var project = new TestProject();
        ProjectContext context = project.Context;
        var settings = new ExtensionSettings {
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = connectionString,
            OutputPath = "Generated",
            Namespace = "TestApp.Generated"
        };
        settings.Queries.Add(new QuerySetting { MethodName = "AotBuildProbe", SourceType = QuerySourceType.Text, Target = "SELECT CAST(1 AS int) AS Id" });
        await ConfigStore.SaveAsync(context, null, settings, CancellationToken.None);

        int exitCode = await Cli.RunAsync(["refresh", "--project", project.ProjectFile], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(project.DirectoryPath, "Generated", "DbCommands.rinku.cs")));
    }
}
