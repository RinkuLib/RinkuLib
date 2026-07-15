using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// How values become <see cref="IDbDataParameter"/>s: inference by default, pinned metadata through
/// <see cref="QueryCommand.UpdateParamCache"/>, and metadata learned back from a live command.
/// </summary>
public class ParameterBindingTests {
    public static IEnumerable<object[]> Values => [
        [42],
        [42L],
        [4.2],
        [4.2m],
        ["text"],
        [true],
        [new DateTime(2024, 5, 1)],
        [Guid.Parse("11111111-2222-3333-4444-555555555555")],
        [new byte[] { 1, 2, 3 }],
    ];

    [Theory]
    [MemberData(nameof(Values))]
    public void Bound_value_is_passed_through_unchanged(object value) {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        var builder = query.StartBuilder();
        builder.Use("@X", value);
        var cmd = Render.From(builder);
        var p = Assert.Single(cmd.BoundParameters);
        Assert.Equal("@X", p.ParameterName);
        Assert.Equal(value, p.Value);
    }

    [Fact]
    public void Parameters_bind_in_template_order_not_use_order() {
        var query = new QueryCommand("SELECT * FROM T WHERE A = ?@A AND B = ?@B AND C = ?@C");
        var builder = query.StartBuilder();
        builder.Use("@C", 3);
        builder.Use("@A", 1);
        builder.Use("@B", 2);
        Render.Expect(builder, "SELECT * FROM T WHERE A = @A AND B = @B AND C = @C",
            ("@A", 1), ("@B", 2), ("@C", 3));
    }

    [Fact]
    public void Uncached_parameter_leaves_type_to_the_driver() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        var builder = query.StartBuilder();
        builder.Use("@X", 5);
        var cmd = Render.From(builder);
        Assert.Equal(default, cmd.BoundParameters[0].DbType);
    }

    [Fact]
    public void Pinned_type_is_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", TypedDbParamCache.Get(DbType.Int64)));
        var builder = query.StartBuilder();
        builder.Use("@X", 5L);
        var cmd = Render.From(builder);
        Assert.Equal(DbType.Int64, cmd.BoundParameters[0].DbType);
    }

    [Fact]
    public void Pinned_size_is_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", SizedDbParamCache.Get(DbType.String, 100)));
        var builder = query.StartBuilder();
        builder.Use("@X", "abc");
        var cmd = Render.From(builder);
        Assert.Equal(DbType.String, cmd.BoundParameters[0].DbType);
        Assert.Equal(100, cmd.BoundParameters[0].Size);
    }

    [Fact]
    public void Pinned_precision_and_scale_are_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", new ScaledDbParamCache(DbType.Decimal, 18, 4)));
        var builder = query.StartBuilder();
        builder.Use("@X", 1.5m);
        var cmd = Render.From(builder);
        var p = cmd.BoundParameters[0];
        Assert.Equal(DbType.Decimal, p.DbType);
        Assert.Equal(18, p.Precision);
        Assert.Equal(4, p.Scale);
    }

    [Fact]
    public void Pinned_direction_is_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", new DirectionalDbParamCache(ParameterDirection.InputOutput, DbType.Int32)));
        var builder = query.StartBuilder();
        builder.Use("@X", 5);
        var cmd = Render.From(builder);
        var p = cmd.BoundParameters[0];
        Assert.Equal(ParameterDirection.InputOutput, p.Direction);
        Assert.Equal(DbType.Int32, p.DbType);
    }

    [Fact]
    public void Pinned_direction_and_size_are_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", new DirectionalSizedDbParamCache(ParameterDirection.Output, DbType.String, 500)));
        var builder = query.StartBuilder();
        builder.Use("@X", "abc");
        var cmd = Render.From(builder);
        var p = cmd.BoundParameters[0];
        Assert.Equal(ParameterDirection.Output, p.Direction);
        Assert.Equal(500, p.Size);
    }

    [Fact]
    public void Pinned_direction_precision_and_scale_are_applied_to_the_parameter() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.True(query.UpdateParamCache("@X", new DirectionalScaledDbParamCache(ParameterDirection.InputOutput, DbType.Decimal, 10, 2)));
        var builder = query.StartBuilder();
        builder.Use("@X", 1.5m);
        var cmd = Render.From(builder);
        var p = cmd.BoundParameters[0];
        Assert.Equal(ParameterDirection.InputOutput, p.Direction);
        Assert.Equal(10, p.Precision);
        Assert.Equal(2, p.Scale);
    }

    [Fact]
    public void Pinning_an_unknown_name_returns_false() {
        var query = new QueryCommand("SELECT * FROM T WHERE X = @X");
        Assert.False(query.UpdateParamCache("@Nope", TypedDbParamCache.Get(DbType.Int32)));
    }

    [Fact]
    public void Pinning_a_condition_name_returns_false() {
        var query = new QueryCommand("SELECT * FROM T WHERE /*Cond*/X = 1");
        Assert.False(query.UpdateParamCache("Cond", TypedDbParamCache.Get(DbType.Int32)));
    }

    [Fact]
    public void Learning_from_a_live_command_marks_the_query_cached() {
        var query = new QueryCommand("SELECT * FROM T WHERE A = @A AND B = ?@B");
        var builder = query.StartBuilder();
        builder.Use("@A", 5);
        builder.Use("@B", "abc");
        Assert.True(query.NeedToCache(builder.Variables));

        var cmd = Render.From(builder);
        cmd.BoundParameters[0].DbType = DbType.Int32;
        cmd.BoundParameters[1].DbType = DbType.String;
        cmd.BoundParameters[1].Size = 50;
        query.UpdateCache((IDbCommand)cmd);

        Assert.False(query.NeedToCache(builder.Variables));
    }

    [Fact]
    public void Learned_metadata_is_applied_on_the_next_render() {
        var query = new QueryCommand("SELECT * FROM T WHERE A = @A AND B = ?@B");
        var builder = query.StartBuilder();
        builder.Use("@A", 5);
        builder.Use("@B", "abc");
        var first = Render.From(builder);
        first.BoundParameters[0].DbType = DbType.Int32;
        first.BoundParameters[1].DbType = DbType.String;
        first.BoundParameters[1].Size = 50;
        query.UpdateCache((IDbCommand)first);

        var second = Render.From(builder);
        Assert.Equal(DbType.Int32, second.BoundParameters[0].DbType);
        Assert.Equal(DbType.String, second.BoundParameters[1].DbType);
        // sizes round up to the next threshold so plans stay shared
        Assert.Equal(100, second.BoundParameters[1].Size);
    }

    [Fact]
    public void Learning_async_completes_synchronously() {
        var query = new QueryCommand("SELECT * FROM T WHERE A = @A");
        var builder = query.StartBuilder();
        builder.Use("@A", 5);
        var cmd = Render.From(builder);
        cmd.BoundParameters[0].DbType = DbType.Int32;
        var task = query.UpdateCacheAsync((IDbCommand)cmd, TestContext.Current.CancellationToken);
        Assert.True(task.IsCompleted);
        Assert.False(query.NeedToCache(builder.Variables));
    }
}
