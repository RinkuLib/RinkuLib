using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class SqlCommandGenerationTests
{
    [Fact]
    public async Task SqlQuery_StillEmbedsSql()
    {
        using var temp = new TempDirectory();
        const string sql = "SELECT AlbumId, Title FROM albums";
        QuerySetting query = CreateQuery(QuerySourceType.Text, sql);

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(sql, [], []));

        Assert.Contains("command.CommandText = @\"SELECT AlbumId, Title FROM albums\";", commandCode);
        Assert.DoesNotContain("GetSqlFile", commandCode);
    }

    [Fact]
    public async Task RelativeSqlFile_UsesConfiguredPathAsRuntimeKey()
    {
        using var temp = new TempDirectory();
        const string discoveredSql = "SELECT AlbumId, Title FROM albums";
        QuerySetting query = CreateQuery(QuerySourceType.FromFile, "Sql/GetAlbums.sql");

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(discoveredSql, [], []));

        Assert.Contains("RinkuPowerTools.GetSqlFile(\"Sql/GetAlbums.sql\")", commandCode);
        Assert.DoesNotContain(discoveredSql, commandCode);
        Assert.DoesNotContain("SqlPath", commandCode);
        Assert.DoesNotContain("static readonly string", commandCode);
    }

    [Fact]
    public async Task AbsoluteSqlFile_UsesAbsoluteConfiguredPathAsRuntimeKey()
    {
        using var temp = new TempDirectory();
        const string discoveredSql = "SELECT AlbumId FROM albums";
        const string path = @"D:\SharedSql\GetAlbums.sql";
        QuerySetting query = CreateQuery(QuerySourceType.FromFile, path);

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(discoveredSql, [], []));

        Assert.Contains("RinkuPowerTools.GetSqlFile(@\"D:\\SharedSql\\GetAlbums.sql\")", commandCode);
        Assert.DoesNotContain(discoveredSql, commandCode);
    }

    [Fact]
    public async Task StoredProcedure_StillUsesProcedureName()
    {
        using var temp = new TempDirectory();
        QuerySetting query = CreateQuery(QuerySourceType.StoredProcedure, "dbo.GetAlbums");

        var (_, commandCode, _) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema("dbo.GetAlbums", [], []));

        Assert.Contains("command.CommandText = @\"dbo.GetAlbums\";", commandCode);
        Assert.Contains("command.CommandType = CommandType.StoredProcedure;", commandCode);
        Assert.DoesNotContain("GetSqlFile", commandCode);
    }

    private static QuerySetting CreateQuery(QuerySourceType sourceType, string target) => new()
    {
        MethodName = "GetAlbums",
        Target = target,
        SourceType = sourceType
    };
}
