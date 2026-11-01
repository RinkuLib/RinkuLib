namespace RinkuPowerTools.Cli.Tests;

internal sealed class TestProject : IDisposable {
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "RinkuPowerTools.Cli.Tests", Guid.NewGuid().ToString("N"));
    public string ProjectFile => Path.Combine(DirectoryPath, "TestApp.csproj");

    public TestProject(string? projectBody = null) {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(ProjectFile, projectBody ?? "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
    }

    public ProjectContext Context => ProjectContext.Resolve(ProjectFile);

    public void Dispose() {
        try {
            Directory.Delete(DirectoryPath, true);
        }
        catch {
        }
    }
}
