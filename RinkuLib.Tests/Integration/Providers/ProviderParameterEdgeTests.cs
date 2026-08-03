using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public sealed class ProviderParameterEdgeFixture : DBFixture<SqlConnection>;

/// <summary>
/// Provider-sensitive parameter cases expressed through Rinku's parameter metadata entrypoints.
/// The SQL Server assertions are only the observation; the binding choice stays in Rinku metadata.
/// </summary>
public sealed class ProviderParameterEdgeTests(ProviderParameterEdgeFixture fixture)
    : IClassFixture<ProviderParameterEdgeFixture> {
    [Fact]
    public async Task A_pinned_ansi_string_uses_the_provider_ansi_representation() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        var query = new QueryCommand("SELECT DATALENGTH(@value)");
        Assert.True(query.UpdateParamCache("@value", TypedDbParamCache.Get(DbType.AnsiString)));

        Assert.Equal(3, await query.QueryAsync<int>(cnn, new { value = "abc" }, ct: ct));
    }

    [Fact]
    public async Task A_provider_read_parameter_cache_preserves_the_provider_metadata() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        var query = new QueryCommand("SELECT DATALENGTH(@value)");
        var length = await query.QueryAsync<int>(cnn, out var cmd, new { value = "abc" }, ct: ct);
        using (cmd) {
            Assert.Equal(6, length);
            Assert.Equal(DbType.String, ((IDbDataParameter)cmd.Parameters[0]).DbType);
        }
    }

    [Fact]
    public async Task Sql_server_system_variables_are_not_treated_as_rinku_parameters() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        var query = new QueryCommand("DECLARE @@Name int; SELECT @@Name = @Id + 1; SELECT @@Name");
        Assert.Equal(2, await query.QueryAsync<int>(cnn, new { Id = 1 }, ct: ct));
    }

    [Fact]
    public async Task A_custom_parameter_info_can_bind_a_sql_server_table_valued_parameter() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TYPE dbo.ProviderIntList AS TABLE (Value INT NOT NULL);";
            await setup.ExecuteNonQueryAsync(ct);
            setup.CommandText = "CREATE PROCEDURE dbo.ProviderSum @ids dbo.ProviderIntList READONLY AS SELECT SUM(Value) FROM @ids;";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var query = new QueryCommand("dbo.ProviderSum", ["ids"]);
            Assert.True(query.UpdateParamCache("@ids", new SqlServerIntTableParam()));
            Assert.Equal(6, await query.QueryAsync<int>(cnn, new { ids = new[] { 1, 2, 3 } }, ct: ct));
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP PROCEDURE dbo.ProviderSum; DROP TYPE dbo.ProviderIntList;";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task A_data_table_can_bind_through_the_custom_parameter_info_entrypoint() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TYPE dbo.ProviderDataTableList AS TABLE (Value INT NOT NULL);";
            await setup.ExecuteNonQueryAsync(ct);
            setup.CommandText = "CREATE PROCEDURE dbo.ProviderDataTableSum @ids dbo.ProviderDataTableList READONLY AS SELECT SUM(Value) FROM @ids;";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var table = new DataTable();
            table.Columns.Add("Value", typeof(int));
            table.Rows.Add(1);
            table.Rows.Add(2);
            table.Rows.Add(3);

            var query = new QueryCommand("dbo.ProviderDataTableSum", ["ids"]);
            Assert.True(query.UpdateParamCache("@ids", new SqlServerIntTableParam("dbo.ProviderDataTableList")));
            Assert.Equal(6, await query.QueryAsync<int>(cnn, new { ids = table }, ct: ct));
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP PROCEDURE dbo.ProviderDataTableSum; DROP TYPE dbo.ProviderDataTableList;";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task A_custom_parameter_info_can_bind_an_empty_sql_server_table_valued_parameter() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TYPE dbo.ProviderEmptyIntList AS TABLE (Value INT NOT NULL);";
            await setup.ExecuteNonQueryAsync(ct);
            setup.CommandText = "CREATE PROCEDURE dbo.ProviderEmptySum @ids dbo.ProviderEmptyIntList READONLY AS SELECT SUM(Value) FROM @ids;";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var query = new QueryCommand("dbo.ProviderEmptySum", ["ids"]);
            Assert.True(query.UpdateParamCache("@ids", new SqlServerIntTableParam("dbo.ProviderEmptyIntList")));
            Assert.Null(await query.QueryAsync<int?>(cnn, new { ids = Array.Empty<int>() }, ct: ct));
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP PROCEDURE dbo.ProviderEmptySum; DROP TYPE dbo.ProviderEmptyIntList;";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task A_custom_table_valued_parameter_can_combine_with_ordinary_parameters() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TYPE dbo.ProviderLabeledIntList AS TABLE (Value INT NOT NULL);";
            await setup.ExecuteNonQueryAsync(ct);
            setup.CommandText = "CREATE PROCEDURE dbo.ProviderLabelSum @ids dbo.ProviderLabeledIntList READONLY, @label nvarchar(20) AS SELECT Value, @label AS Label FROM @ids;";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var query = new QueryCommand("dbo.ProviderLabelSum", ["ids", "label"]);
            Assert.True(query.UpdateParamCache("@ids", new SqlServerIntTableParam("dbo.ProviderLabeledIntList")));
            var rows = await query.QueryAsync<List<ProviderLabeledIntRow>>(
                cnn,
                new { ids = new[] { 1, 2, 3 }, label = "from-parameter" },
                ct: ct);

            Assert.Equal(
                [
                    new ProviderLabeledIntRow { Value = 1, Label = "from-parameter" },
                    new ProviderLabeledIntRow { Value = 2, Label = "from-parameter" },
                    new ProviderLabeledIntRow { Value = 3, Label = "from-parameter" },
                ],
                rows);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP PROCEDURE dbo.ProviderLabelSum; DROP TYPE dbo.ProviderLabeledIntList;";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}

sealed class SqlServerIntTableParam(string typeName = "dbo.ProviderIntList") : DbParamInfo(true) {
    private readonly string _typeName = typeName;

    public override bool SaveUse(string name, IDbCommand cmd, ref object value) {
        var parameter = Add(name, cmd, GetTable(value));
        value = parameter;
        return true;
    }

    public override bool Update(IDbCommand cmd, ref object? current, object? newValue) {
        if (current is not IDbDataParameter parameter || newValue is not (int[] or DataTable))
            return false;
        parameter.Value = GetTable(newValue);
        return true;
    }

    public override bool Use(string name, IDbCommand cmd, object value) {
        Add(name, cmd, GetTable(value));
        return true;
    }

    public override bool Use(string name, DbCommand cmd, object value) {
        Add(name, cmd, GetTable(value));
        return true;
    }

    public override void Remove(IDbCommand cmd, object current)
        => DbParamInfo.RemoveSingle(((IDbDataParameter)current).ParameterName, cmd);

    private SqlParameter Add(string name, IDbCommand cmd, DataTable table) {
        var parameter = new SqlParameter(name, SqlDbType.Structured) {
            TypeName = _typeName,
            Value = table,
        };
        cmd.Parameters.Add(parameter);
        return parameter;
    }

    private static DataTable GetTable(object value) => value switch {
        int[] values => ToTable(values),
        DataTable table => table,
        _ => throw new ArgumentException("Expected an int array or DataTable.", nameof(value)),
    };

    private static DataTable ToTable(int[] values) {
        var table = new DataTable();
        table.Columns.Add("Value", typeof(int));
        foreach (var value in values)
            table.Rows.Add(value);
        return table;
    }
}

sealed class ProviderLabeledIntRow {
    public int Value { get; set; }
    public string Label { get; set; } = null!;

    public override bool Equals(object? obj)
        => obj is ProviderLabeledIntRow other && Value == other.Value && Label == other.Label;

    public override int GetHashCode() => HashCode.Combine(Value, Label);
}
