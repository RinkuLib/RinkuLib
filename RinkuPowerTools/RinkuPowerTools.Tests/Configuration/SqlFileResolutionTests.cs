using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Configuration;

public class SqlFileResolutionTests
{
    [Fact]
    public async Task RelativeSqlFile_IsReadFromProjectDirectory()
    {
        using var temp = new TempDirectory();
        string sqlDirectory = System.IO.Path.Combine(temp.Path, "Sql");
        Directory.CreateDirectory(sqlDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(sqlDirectory, "GetAlbums.sql"),
            "SELECT 1",
            TestContext.Current.CancellationToken);

        var settings = CreateSettings(temp.Path);
        var query = new QuerySetting
        {
            MethodName = "GetAlbums",
            Target = "Sql/GetAlbums.sql",
            SourceType = QuerySourceType.FromFile
        };

        string sql = await settings.GetSqlTextAsync(query, CancellationToken.None);

        Assert.Equal("SELECT 1", sql);
    }

    [Fact]
    public async Task RelativeSqlFile_OutsideProject_IsRejected()
    {
        using var temp = new TempDirectory();
        string projectDirectory = System.IO.Path.Combine(temp.Path, "Project");
        Directory.CreateDirectory(projectDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "Outside.sql"),
            "SELECT 3",
            TestContext.Current.CancellationToken);

        var settings = CreateSettings(projectDirectory);
        var query = new QuerySetting
        {
            MethodName = "Outside",
            Target = "../Outside.sql",
            SourceType = QuerySourceType.FromFile
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => settings.GetSqlTextAsync(query, CancellationToken.None));
    }

    [Fact]
    public async Task AbsoluteSqlFile_IsReadDirectly()
    {
        using var temp = new TempDirectory();
        string path = System.IO.Path.Combine(temp.Path, "GetAlbums.sql");
        await File.WriteAllTextAsync(path, "SELECT 2", TestContext.Current.CancellationToken);

        var settings = CreateSettings(System.IO.Path.Combine(temp.Path, "OtherProject"));
        var query = new QuerySetting
        {
            MethodName = "GetAlbums",
            Target = path,
            SourceType = QuerySourceType.FromFile
        };

        string sql = await settings.GetSqlTextAsync(query, CancellationToken.None);

        Assert.Equal("SELECT 2", sql);
    }


    [Fact]
    public void SelectedSqlFile_InsideProject_IsStoredRelative()
    {
        using var temp = new TempDirectory();
        string projectDirectory = System.IO.Path.Combine(temp.Path, "Project");
        string sqlPath = System.IO.Path.Combine(projectDirectory, "Sql", "GetAlbums.sql");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sqlPath)!);

        var settings = CreateSettings(projectDirectory);

        Assert.Equal("Sql/GetAlbums.sql", settings.GetSqlFileReference(sqlPath));
    }

    [Fact]
    public void SelectedSqlFile_OutsideProject_IsStoredAbsolute()
    {
        using var temp = new TempDirectory();
        string projectDirectory = System.IO.Path.Combine(temp.Path, "Project");
        string sqlPath = System.IO.Path.Combine(temp.Path, "Shared", "GetAlbums.sql");
        Directory.CreateDirectory(projectDirectory);

        var settings = CreateSettings(projectDirectory);

        Assert.Equal(System.IO.Path.GetFullPath(sqlPath), settings.GetSqlFileReference(sqlPath));
    }

    [Fact]
    public void SelectedSqlFile_ProjectRoot_IsStoredAsFileName()
    {
        using var temp = new TempDirectory();
        string projectDirectory = System.IO.Path.Combine(temp.Path, "Project");
        string sqlPath = System.IO.Path.Combine(projectDirectory, "GetAlbums.sql");
        Directory.CreateDirectory(projectDirectory);

        var settings = CreateSettings(projectDirectory);

        Assert.Equal("GetAlbums.sql", settings.GetSqlFileReference(sqlPath));
    }

    [Fact]
    public void SelectedSqlFile_ProjectPrefixCollision_IsOutside()
    {
        using var temp = new TempDirectory();
        string projectDirectory = System.IO.Path.Combine(temp.Path, "MyApp");
        string sqlPath = System.IO.Path.Combine(temp.Path, "MyApp2", "GetAlbums.sql");
        Directory.CreateDirectory(projectDirectory);

        var settings = CreateSettings(projectDirectory);

        Assert.Equal(System.IO.Path.GetFullPath(sqlPath), settings.GetSqlFileReference(sqlPath));
    }

    private static ExtensionSettings CreateSettings(string projectDirectory)
    {
        var settings = new ExtensionSettings
        {
            ConnectionSourceType = ConnectionSourceType.RawConnectionString,
            ConnectionTarget = "unused"
        };
        settings.SetProjectDirectory(projectDirectory);
        return settings;
    }
}
