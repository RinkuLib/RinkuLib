using System.Data;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public class ParameterGenerationTests
{
    [Fact]
    public async Task InputParameter_IsAddedWithGeneratedMethodArgument()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("@artistId", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0);

        var (_, commandCode, _) = await GenerateAsync(temp.Path, [parameter]);

        Assert.Contains("GetAlbums(this DbConnection connection, int artistId)", commandCode);
        Assert.Contains("command.Add(\"@artistId\", DbType.Int32, (object)artistId);", commandCode);
    }

    [Fact]
    public async Task NullableParameter_UsesDbNull()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("@title", DbType.String, true, 50, ParameterDirection.Input, 0, 0);

        var (_, commandCode, _) = await GenerateAsync(temp.Path, [parameter]);

        Assert.Contains("string? title", commandCode);
        Assert.Contains("(object?)title ?? DBNull.Value", commandCode);
        Assert.Contains(", 50);", commandCode);
    }

    [Fact]
    public async Task OutputParameter_IsReturnedAndConfigured()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("@count", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0);

        var (_, commandCode, _) = await GenerateAsync(temp.Path, [parameter]);

        Assert.Contains("out DbParameter out_count", commandCode);
        Assert.Contains("out_count = command.Add(\"@count\", DbType.Int32, DBNull.Value);", commandCode);
        Assert.Contains("out_count.Direction = ParameterDirection.Output;", commandCode);
    }

    [Fact]
    public async Task InputOutputParameter_KeepsInputValueAndReturnsParameter()
    {
        using var temp = new TempDirectory();
        var parameter = new ParameterMetadata("@total", DbType.Decimal, false, 0, ParameterDirection.InputOutput, 18, 2);

        var (_, commandCode, _) = await GenerateAsync(temp.Path, [parameter]);

        Assert.Contains("decimal total, out DbParameter out_total", commandCode);
        Assert.Contains("out_total = command.Add(\"@total\", DbType.Decimal, total);", commandCode);
        Assert.Contains("out_total.Direction = ParameterDirection.InputOutput;", commandCode);
        Assert.Contains("out_total.Precision = 18;", commandCode);
        Assert.Contains("out_total.Scale = 2;", commandCode);
    }

    private static Task<(string CommandPath, string CommandCode, string SupportCode)> GenerateAsync(
        string projectDirectory,
        List<ParameterMetadata> parameters) =>
        GeneratorTestHelper.GenerateAsync(
            projectDirectory,
            new QuerySetting
            {
                MethodName = "GetAlbums",
                Target = "SELECT 1",
                SourceType = QuerySourceType.Text
            },
            new DiscoveredSchema("SELECT 1", parameters, []));
}
