using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Connections;

public class ConnectionResolverTests
{
    [Fact]
    public async Task RawConnectionString_ReturnsTarget()
    {
        const string expected = "Server=.;Database=Rinku;Trusted_Connection=True";

        string result = await ConnectionResolver.ResolveAsync(
            ConnectionSourceType.RawConnectionString,
            expected,
            null,
            string.Empty,
            CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task EnvironmentVariable_ReadsValue()
    {
        string name = "RINKU_POWERTOOLS_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "Server=test");
        try
        {
            string result = await ConnectionResolver.ResolveAsync(
                ConnectionSourceType.EnvironmentVariable,
                name,
                null,
                string.Empty,
                CancellationToken.None);

            Assert.Equal("Server=test", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task JsonFile_UsesColonSeparatedExtractionPath()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "appsettings.json"),
            """{"ConnectionStrings":{"Main":"Server=json"}}""",
            TestContext.Current.CancellationToken);

        string result = await ResolveFileAsync(
            temp.Path,
            ConnectionSourceType.JsonFile,
            "appsettings.json",
            "ConnectionStrings:Main");

        Assert.Equal("Server=json", result);
    }

    [Fact]
    public async Task XmlFile_UsesXPath()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "connections.xml"),
            """<configuration><connectionStrings><add name="Main" connectionString="Server=xml" /></connectionStrings></configuration>""",
            TestContext.Current.CancellationToken);

        string result = await ResolveFileAsync(
            temp.Path,
            ConnectionSourceType.XmlFile,
            "connections.xml",
            "/configuration/connectionStrings/add[@name='Main']/@connectionString");

        Assert.Equal("Server=xml", result);
    }

    [Fact]
    public async Task DotEnvFile_ReadsNamedVariableCaseInsensitively()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, ".env"),
            "# ignored\nMAIN_CONNECTION=\"Server=dotenv\"\n",
            TestContext.Current.CancellationToken);

        string result = await ResolveFileAsync(
            temp.Path,
            ConnectionSourceType.DotEnvFile,
            ".env",
            "main_connection");

        Assert.Equal("Server=dotenv", result);
    }

    [Fact]
    public async Task IniFile_ReadsSectionKeyCaseInsensitively()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "connections.ini"),
            "[Database]\nConnectionString=Server=ini\n",
            TestContext.Current.CancellationToken);

        string result = await ResolveFileAsync(
            temp.Path,
            ConnectionSourceType.IniFile,
            "connections.ini",
            "[database]connectionstring");

        Assert.Equal("Server=ini", result);
    }

    [Fact]
    public async Task MsBuildProject_ReadsPropertyByLocalName()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "App.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><RinkuConnection>Server=msbuild</RinkuConnection></PropertyGroup></Project>""",
            TestContext.Current.CancellationToken);

        string result = await ResolveFileAsync(
            temp.Path,
            ConnectionSourceType.MsBuildProject,
            "App.csproj",
            "rinkUConnection");

        Assert.Equal("Server=msbuild", result);
    }

    [Fact]
    public async Task LaunchSettings_ReadsProfileEnvironmentVariable()
    {
        using var temp = new TempDirectory();
        string properties = System.IO.Path.Combine(temp.Path, "Properties");
        Directory.CreateDirectory(properties);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(properties, "launchSettings.json"),
            """{"profiles":{"App":{"environmentVariables":{"ConnectionString":"Server=launch"}}}}""",
            TestContext.Current.CancellationToken);

        string result = await ConnectionResolver.ResolveAsync(
            ConnectionSourceType.LaunchSettings,
            "unused",
            "App:ConnectionString",
            temp.Path,
            CancellationToken.None);

        Assert.Equal("Server=launch", result);
    }

    [Fact]
    public async Task UnsupportedVsDataConnection_RemainsExplicit()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => ConnectionResolver.ResolveAsync(
                ConnectionSourceType.VsDataConnection,
                "Main",
                null,
                string.Empty,
                CancellationToken.None));
    }

    [Fact]
    public async Task EmptyTarget_IsRejectedBeforeSourceResolution()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ConnectionResolver.ResolveAsync(
                ConnectionSourceType.RawConnectionString,
                " ",
                null,
                string.Empty,
                CancellationToken.None));
    }

    private static Task<string> ResolveFileAsync(
        string projectDirectory,
        ConnectionSourceType sourceType,
        string target,
        string extractionPath) =>
        ConnectionResolver.ResolveAsync(
            sourceType,
            target,
            extractionPath,
            projectDirectory,
            CancellationToken.None);
}
