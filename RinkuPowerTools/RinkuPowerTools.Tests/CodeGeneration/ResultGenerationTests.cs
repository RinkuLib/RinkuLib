using System.Data;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class ResultGenerationTests
{
    [Fact]
    public async Task SingleSimpleColumn_UsesTheColumnTypeWithoutDto()
    {
        using var temp = new TempDirectory();
        var column = new ParameterMetadata("Count", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0);

        var (_, commandCode, _) = await GenerateAsync(temp.Path, null, [column]);

        Assert.DoesNotContain("<Command", commandCode);
        Assert.DoesNotContain("record GetAlbumsResult", commandCode);
    }

    [Fact]
    public async Task MultipleColumns_GenerateResultRecord()
    {
        using var temp = new TempDirectory();
        var columns = new List<ParameterMetadata>
        {
            new("AlbumId", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0),
            new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)
        };

        var (_, commandCode, _) = await GenerateAsync(temp.Path, null, columns);

        Assert.DoesNotContain("<Command", commandCode);
        Assert.Contains("[Schema(\"", commandCode);
        Assert.Contains("public partial record GetAlbumsResult(int AlbumId, string Title);", commandCode);
    }

    [Fact]
    public async Task ResultSetName_ReplacesDefaultGeneratedRecordName()
    {
        using var temp = new TempDirectory();
        var columns = new List<ParameterMetadata>
        {
            new("AlbumId", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0),
            new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)
        };

        var (_, commandCode, _) = await GenerateAsync(temp.Path, "AlbumRow", columns);

        Assert.DoesNotContain("<Command", commandCode);
        Assert.Contains("public partial record AlbumRow(int AlbumId, string Title);", commandCode);
        Assert.DoesNotContain("record GetAlbumsResult", commandCode);
    }

    [Fact]
    public async Task ResultColumnWithCleanedName_GeneratesTrueName()
    {
        using var temp = new TempDirectory();
        var columns = new List<ParameterMetadata>
        {
            new("Album Id", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0),
            new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)
        };

        var (_, commandCode, _) = await GenerateAsync(temp.Path, null, columns);

        Assert.Contains("[TrueName(\"Album Id\")] int Album_Id", commandCode);
    }

    [Fact]
    public async Task Refresh_shortens_schema_reference_without_removing_custom_record_members() {
        using var temp = new TempDirectory();
        List<ParameterMetadata> columns = [new("Id", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0), new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)];
        var (path, commands, _) = await GenerateAsync(temp.Path, null, columns);
        string previous = commands.Replace("[Schema(", "[global::RinkuPowerTools.Generated.Schema(").Replace("record GetAlbumsResult(int Id, string Title);", "record GetAlbumsResult(int Id, string Title) { public string Display => Title; }");
        await File.WriteAllTextAsync(path, previous, TestContext.Current.CancellationToken);
        var (_, refreshed, support) = await GenerateAsync(temp.Path, null, columns);
        Assert.Contains("[Schema(", refreshed); Assert.Contains("Display => Title", refreshed);
        Assert.DoesNotContain("global::", refreshed); Assert.DoesNotContain("<Command", refreshed);
        GeneratedAssemblyCompiler.Compile(support, refreshed);
    }

    private static Task<(string CommandPath, string CommandCode, string SupportCode)> GenerateAsync(
        string projectDirectory,
        string? resultSetName,
        List<ParameterMetadata> columns) =>
        GeneratorTestHelper.GenerateAsync(
            projectDirectory,
            new QuerySetting
            {
                MethodName = "GetAlbums",
                ResultSetName = resultSetName,
                Target = "SELECT 1",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT 1", [], columns));
}
