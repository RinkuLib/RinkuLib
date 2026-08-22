using System.Text.Json;

namespace RinkuPowerTools.Tests.Configuration;

public class QuerySettingSerializationTests
{
    [Fact]
    public void ResultSetName_RoundTrips()
    {
        var query = new QuerySetting
        {
            MethodName = "GetAlbums",
            ResultSetName = "AlbumRow",
            Target = "SELECT AlbumId FROM albums",
            SourceType = QuerySourceType.Text,
            Parameters =
            [
                new ParameterOverride
                {
                    Name = "@artistId",
                    Type = "int",
                    IsNullable = false
                }
            ]
        };

        string json = JsonSerializer.Serialize(query);
        QuerySetting? roundTrip = JsonSerializer.Deserialize<QuerySetting>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("AlbumRow", roundTrip.ResultSetName);
        Assert.Single(roundTrip.Parameters);
        Assert.Equal("@artistId", roundTrip.Parameters[0].Name);
    }
}
