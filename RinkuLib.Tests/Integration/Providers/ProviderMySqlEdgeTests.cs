using MySqlConnector;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public sealed class ProviderMySqlEdgeFixture : DBFixture<MySqlConnection>;

public sealed class ProviderMySqlEdgeTests(ProviderMySqlEdgeFixture fixture)
    : IClassFixture<ProviderMySqlEdgeFixture> {
    [Fact]
    public async Task MySql_tinyint_bool_and_unsigned_values_map_through_the_normal_result_path() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TABLE provider_flags (Active TINYINT(1) NOT NULL, Amount BIGINT UNSIGNED NOT NULL); INSERT INTO provider_flags VALUES (1, 4000000000);";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var row = new QueryCommand("SELECT Active, Amount FROM provider_flags")
                .Query<MySqlFlags>(cnn);
            Assert.True(row.Active);
            Assert.Equal(4_000_000_000UL, row.Amount);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_flags";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task MySql_nullable_bool_preserves_null_zero_and_one() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_nullable_flags (Id INT NOT NULL, Value BOOL NULL); INSERT INTO provider_nullable_flags VALUES (1,NULL),(2,0),(3,1);";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            var values = new QueryCommand("SELECT Value FROM provider_nullable_flags ORDER BY Id")
                .Query<List<bool?>>(cnn);
            Assert.Equal([null, false, true], values);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_nullable_flags";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task MySql_time_values_preserve_ticks_through_mapping() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_times (Value TIME NOT NULL); INSERT INTO provider_times VALUES ('15:24:00.000000');";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            var value = new QueryCommand("SELECT Value FROM provider_times").Query<TimeSpan>(cnn);
            Assert.Equal(TimeSpan.FromTicks(15 * TimeSpan.TicksPerHour + 24 * TimeSpan.TicksPerMinute), value);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_times";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task MySql_async_reader_path_preserves_provider_column_shape() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_reader (Id INT NOT NULL, Active TINYINT NOT NULL); INSERT INTO provider_reader VALUES (1,1),(2,0),(3,1);";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            await using var reader = await cnn.ExecuteReaderAsync("SELECT Id, Active FROM provider_reader WHERE Id < @id", out _, new { id = 42 }, ct: ct);
            var rows = new List<(int Id, bool Active)>();
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetInt32(0), reader.GetBoolean(1)));
            Assert.Equal([(1, true), (2, false), (3, true)], rows);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_reader";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task MySql_sync_reader_path_preserves_provider_column_shape() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_sync_reader (Id INT NOT NULL, Active TINYINT NOT NULL); INSERT INTO provider_sync_reader VALUES (1,1),(2,0),(3,1);";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            using var reader = cnn.ExecuteReader("SELECT Id, Active FROM provider_sync_reader WHERE Id < @id", out _, new { id = 42 });
            var rows = new List<(int Id, bool Active)>();
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.GetBoolean(1)));
            Assert.Equal([(1, true), (2, false), (3, true)], rows);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_sync_reader";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}

public sealed record MySqlFlags(bool Active, ulong Amount);
