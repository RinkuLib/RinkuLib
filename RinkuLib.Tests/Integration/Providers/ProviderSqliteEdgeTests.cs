using System.Globalization;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public sealed class ProviderSqliteEdgeTests {
    [Fact]
    public async Task Sqlite_async_mapping_can_reuse_the_same_shape() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = new SqliteConnection("Data Source=:memory:");
        await cnn.OpenAsync(ct);
        Assert.Equal(42, await new QueryCommand("SELECT 42 AS Id").QueryAsync<int>(cnn, ct: ct));
        Assert.Equal(42, await new QueryCommand("SELECT 42 AS Id").QueryAsync<int>(cnn, ct: ct));
    }

    [Fact]
    public void Sqlite_enum_parameters_round_trip_through_registered_conversion() {
        using var cnn = new SqliteConnection("Data Source=:memory:");
        cnn.Open();
        var query = new QueryCommand("SELECT @value AS Value, @nullable AS NullableValue, @missing AS MissingValue");
        var row = query.Query<SqliteEnumRow>(cnn, new {
            value = SqliteEnum.B,
            nullable = (SqliteEnum?)SqliteEnum.B,
            missing = DBNull.Value,
        });

        Assert.Equal(SqliteEnum.B, row.Value);
        Assert.Equal(SqliteEnum.B, row.NullableValue);
        Assert.Null(row.MissingValue);
    }

    [Fact]
    public void Sqlite_date_values_are_read_independently_of_the_current_culture() {
        using var cnn = new SqliteConnection("Data Source=:memory:");
        cnn.Open();
        using var create = cnn.CreateCommand();
        create.CommandText = "CREATE TABLE People (Id INTEGER, DoB DATETIME); INSERT INTO People VALUES (1, '2019-07-31 01:00:00');";
        create.ExecuteNonQuery();

        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fa-IR");

            var query = new QueryCommand("SELECT DoB FROM People");
            var row = query.Query<DateTime>(cnn);
            Assert.Equal(new DateTime(2019, 7, 31, 1, 0, 0), row);
        }
        finally {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }
}

public enum SqliteEnum : byte { A = 1, B = 2 }
public sealed class SqliteEnumRow {
    public SqliteEnum Value { get; set; }
    public SqliteEnum? NullableValue { get; set; }
    public SqliteEnum? MissingValue { get; set; }
}
