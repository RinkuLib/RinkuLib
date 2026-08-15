using Rinku;
using Rinku.Querying;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

public class EnumParameterTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    [Fact]
    public void Enum_parameters_round_trip_through_the_database() {
        var query = new QueryCommand("SELECT @mode");
        using var cnn = Db.GetConnection();

        Assert.Equal(RoundTripMode.Active, query.Query<RoundTripMode>(cnn, new { mode = RoundTripMode.Active }));
    }

    [Fact]
    public void Nullable_enum_results_accept_a_database_null_after_a_typed_value() {
        var query = new QueryCommand("SELECT CAST(1 AS INTEGER) UNION ALL SELECT NULL");
        using var cnn = Db.GetConnection();

        Assert.Equal([RoundTripMode.Active, null], query.Query<List<RoundTripMode?>>(cnn));
    }

    private enum RoundTripMode : short {
        None,
        Active,
    }
}
