using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Npgsql;
using Rinku;
using Rinku.Mapping;
using Rinku.Querying;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public sealed class ProviderPostgresEdgeFixture : DBFixture<NpgsqlConnection>;

/// <summary>
/// A provider-native PostgreSQL array parameter supplied through Rinku's complete parameter takeover API.
/// </summary>
public sealed class ProviderPostgresEdgeTests(ProviderPostgresEdgeFixture fixture)
    : IClassFixture<ProviderPostgresEdgeFixture> {
    [Fact]
    public async Task A_custom_parameter_info_can_bind_a_native_postgres_array() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        await using (var setup = cnn.CreateCommand()) {
            setup.CommandText = "CREATE TABLE provider_array_values (id integer NOT NULL); INSERT INTO provider_array_values VALUES (1),(2),(3),(4),(5);";
            await setup.ExecuteNonQueryAsync(ct);
        }

        try {
            var query = new QueryCommand("SELECT id FROM provider_array_values WHERE id = ANY(@ids) ORDER BY id");
            Assert.True(query.UpdateParamCache("@ids", new PostgresIntArrayParam()));

            var values = new List<int>();
            await foreach (var value in query.StreamQueryAsync<int>(cnn, new { ids = new[] { 1, 3, 5 } }, ct: ct))
                values.Add(value);
            Assert.Equal([1, 3, 5], values);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_array_values";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task PostgreSQL_list_values_can_bind_through_the_same_native_array_entrypoint() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_array_list (id integer NOT NULL); INSERT INTO provider_array_list VALUES (1),(2),(3),(4),(5);";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            var query = new QueryCommand("SELECT id FROM provider_array_list WHERE id = ANY(@ids) ORDER BY id");
            Assert.True(query.UpdateParamCache("@ids", new PostgresIntArrayParam()));
            var values = await query.QueryAsync<List<int>>(cnn, new { ids = new List<int> { 1, 3, 5 } }, ct: ct);
            Assert.Equal([1, 3, 5], values);
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_array_list";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task PostgreSQL_char_values_map_as_chars() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        await using var setup = cnn.CreateCommand();
        setup.CommandText = "CREATE TABLE provider_chars (value CHAR(1) NOT NULL); INSERT INTO provider_chars VALUES ('a');";
        await setup.ExecuteNonQueryAsync(ct);
        try {
            Assert.Equal('a', new QueryCommand("SELECT value FROM provider_chars").Query<char>(cnn));
        }
        finally {
            await using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DROP TABLE provider_chars";
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task PostgreSQL_nullable_datetime_parameters_round_trip() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);
        var now = DateTime.UtcNow;
        var value = await new QueryCommand("SELECT @now")
            .QueryAsync<DateTime>(cnn, new { now }, ct: ct);
        Assert.Equal(now.Ticks / 10 * 10, value.Ticks);
    }

    [Fact]
    public async Task A_custom_type_parsing_info_can_emit_a_provider_native_array_read() {
        var ct = TestContext.Current.CancellationToken;
        await using var cnn = fixture.GetConnection();
        await cnn.OpenAsync(ct);

        TypeParsingInfo.AddOrSet(typeof(int[]), PostgresIntArrayTypeInfo.Instance);
        using var query = new QueryCommand("SELECT ARRAY[1,2,3]");
        ITypeParser<int[]>? parser = null;
        try {
            var values = query.Query<int[]>(cnn);
            Assert.Equal([1, 2, 3], values);
            Assert.True(query.TryGetCachedParser<int[]>(Span<bool>.Empty, out parser));
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParsingInfo.TryRemove(typeof(int[]), out _);
        }
    }
}

sealed class PostgresIntArrayTypeInfo : ScalarTypeParsingInfo<int[]> {
    internal static readonly PostgresIntArrayTypeInfo Instance = new();

    protected override DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter, ColumnInfo column, int ordinal)
        => column.Type == typeof(Array)
            ? new PostgresIntArrayPlan(parentType, parameter.NameComparer.GetDefaultName(), parameter.NullColHandler, ordinal)
            : null;
}

sealed class PostgresIntArrayPlan(Type parentType, string parameterName, INullColHandler nullHandler, int ordinal)
    : ScalarDbItemPlan<int[]>(parentType, parameterName, nullHandler, ordinal) {
    private static readonly MethodInfo ReadMethod = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!.MakeGenericMethod(typeof(int[]));

    protected override void EmitValue(ColumnInfo column, Generator generator) {
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, ColumnOrdinal);
        generator.Emit(OpCodes.Callvirt, ReadMethod);
    }
}

sealed class PostgresIntArrayParam : DbParamInfo {
    public PostgresIntArrayParam() : base(true) { }

    public override bool SaveUse(string name, IDbCommand cmd, ref object value) {
        var parameter = Add(name, cmd, ToArray(value));
        value = parameter;
        return true;
    }

    public override bool Update(IDbCommand cmd, ref object? current, object? newValue) {
        if (current is not IDbDataParameter parameter || newValue is null)
            return false;
        parameter.Value = ToArray(newValue);
        return true;
    }

    public override bool Use(string name, IDbCommand cmd, object value) {
        Add(name, cmd, ToArray(value));
        return true;
    }

    public override bool Use(string name, DbCommand cmd, object value) {
        Add(name, cmd, ToArray(value));
        return true;
    }

    public override void Remove(IDbCommand cmd, object current)
        => DbParamInfo.RemoveSingle(((IDbDataParameter)current).ParameterName, cmd);

    private static NpgsqlParameter Add(string name, IDbCommand cmd, int[] values) {
        var parameter = new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) {
            Value = values,
        };
        cmd.Parameters.Add(parameter);
        return parameter;
    }

    private static int[] ToArray(object value) => value switch {
        int[] values => values,
        IEnumerable<int> values => values.ToArray(),
        _ => throw new InvalidCastException($"Expected integer sequence, got {value.GetType()}.")
    };
}
