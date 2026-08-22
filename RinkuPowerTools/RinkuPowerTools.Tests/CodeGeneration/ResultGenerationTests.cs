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

        var generated = await GenerateAsync(temp.Path, null, [column]);

        Assert.Contains("/// <Command cref=\"int\" />", generated.CommandCode);
        Assert.DoesNotContain("record GetAlbumsResult", generated.CommandCode);
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

        var generated = await GenerateAsync(temp.Path, null, columns);

        Assert.Contains("/// <Command cref=\"GetAlbumsResult\" />", generated.CommandCode);
        Assert.Contains("public partial record GetAlbumsResult(int AlbumId, string Title);", generated.CommandCode);
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

        var generated = await GenerateAsync(temp.Path, "AlbumRow", columns);

        Assert.Contains("/// <Command cref=\"AlbumRow\" />", generated.CommandCode);
        Assert.Contains("public partial record AlbumRow(int AlbumId, string Title);", generated.CommandCode);
        Assert.DoesNotContain("record GetAlbumsResult", generated.CommandCode);
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

        var generated = await GenerateAsync(temp.Path, null, columns);

        Assert.Contains("[TrueName(\"Album Id\")] int Album_Id", generated.CommandCode);
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
