using System.Data;

namespace RinkuPowerTools.Tests.Database;

public class ParameterMetadataTests
{
    [Theory]
    [InlineData("$1", "p1")]
    [InlineData("@artistId", "artistId")]
    [InlineData(":artist-id", "artist_id")]
    public void CleanName_IsValidForProviderParameterNames(string dbName, string expected)
    {
        var metadata = new ParameterMetadata(dbName, DbType.Int32, false, 0, ParameterDirection.Input, 0, 0);

        Assert.Equal(expected, metadata.CleanName);
    }

    [Fact]
    public void Binding_IsExplicitAndIndependentFromParameterName()
    {
        var postgres = new ParameterMetadata(
            "$1",
            DbType.Int32,
            false,
            0,
            ParameterDirection.Input,
            0,
            0,
            binding: ParameterBinding.Positional);
        var sqlite = new ParameterMetadata(
            "$1",
            DbType.Int32,
            false,
            0,
            ParameterDirection.Input,
            0,
            0);

        Assert.Equal(ParameterBinding.Positional, postgres.Binding);
        Assert.Equal(ParameterBinding.Named, sqlite.Binding);
    }

    [Fact]
    public void MissingDbType_KeepsObjectShapeWithoutInventingDbType()
    {
        var metadata = new ParameterMetadata("$value", null, true, 0, ParameterDirection.Input, 0, 0, "object");

        Assert.Null(metadata.DbType);
        Assert.Equal("object?", metadata.CSharpType);
    }
}
