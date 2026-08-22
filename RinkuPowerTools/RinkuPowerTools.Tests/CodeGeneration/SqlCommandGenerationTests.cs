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

        var result = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(sql, [], []));

        Assert.Contains("command.CommandText = @\"SELECT AlbumId, Title FROM albums\";", result.CommandCode);
        Assert.DoesNotContain("GetSqlFile", result.CommandCode);
    }

    [Fact]
    public async Task RelativeSqlFile_UsesConfiguredPathAsRuntimeKey()
    {
        using var temp = new TempDirectory();
        const string discoveredSql = "SELECT AlbumId, Title FROM albums";
        QuerySetting query = CreateQuery(QuerySourceType.FromFile, "Sql/GetAlbums.sql");

        var result = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(discoveredSql, [], []));

        Assert.Contains("RinkuPowerTools.GetSqlFile(\"Sql/GetAlbums.sql\")", result.CommandCode);
        Assert.DoesNotContain(discoveredSql, result.CommandCode);
        Assert.DoesNotContain("SqlPath", result.CommandCode);
        Assert.DoesNotContain("static readonly string", result.CommandCode);
    }

    [Fact]
    public async Task AbsoluteSqlFile_UsesAbsoluteConfiguredPathAsRuntimeKey()
    {
        using var temp = new TempDirectory();
        const string discoveredSql = "SELECT AlbumId FROM albums";
        const string path = @"D:\SharedSql\GetAlbums.sql";
        QuerySetting query = CreateQuery(QuerySourceType.FromFile, path);

        var result = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema(discoveredSql, [], []));

        Assert.Contains("RinkuPowerTools.GetSqlFile(@\"D:\\SharedSql\\GetAlbums.sql\")", result.CommandCode);
        Assert.DoesNotContain(discoveredSql, result.CommandCode);
    }

    [Fact]
    public async Task StoredProcedure_StillUsesProcedureName()
    {
        using var temp = new TempDirectory();
        QuerySetting query = CreateQuery(QuerySourceType.StoredProcedure, "dbo.GetAlbums");

        var result = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema("dbo.GetAlbums", [], []));

        Assert.Contains("command.CommandText = @\"dbo.GetAlbums\";", result.CommandCode);
        Assert.Contains("command.CommandType = CommandType.StoredProcedure;", result.CommandCode);
        Assert.DoesNotContain("GetSqlFile", result.CommandCode);
    }

    private static QuerySetting CreateQuery(QuerySourceType sourceType, string target) => new()
    {
        MethodName = "GetAlbums",
        Target = target,
        SourceType = sourceType
    };
}
