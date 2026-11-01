namespace RinkuPowerTools.Cli.Tests;

public class ProjectContextTests {
    [Fact]
    public void UsesRootNamespace() {
        using var project = new TestProject("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><RootNamespace>My.App.Data</RootNamespace></PropertyGroup></Project>");
        Assert.Equal("My.App.Data", project.Context.BaseNamespace);
    }

    [Fact]
    public void FallsBackToAssemblyName() {
        using var project = new TestProject("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>My.App</AssemblyName></PropertyGroup></Project>");
        Assert.Equal("My.App", project.Context.BaseNamespace);
    }

    [Fact]
    public void FallsBackToProjectName() {
        using var project = new TestProject();
        Assert.Equal("TestApp", project.Context.BaseNamespace);
    }

    [Fact]
    public void ResolvesProjectDirectory() {
        using var project = new TestProject();
        ProjectContext context = ProjectContext.Resolve(project.DirectoryPath);
        Assert.Equal(project.ProjectFile, context.ProjectFile);
    }
}
