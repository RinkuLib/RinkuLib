using System.Collections;
using System.Data;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Rendering a template into the SQL a run sends: the base handlers (<c>_S</c>, <c>_R</c>, <c>_N</c>), the
/// spread (<c>_X</c>) with every value shape, custom letters, and the error paths. Expected output follows
/// <c>docs/articles/conditional-sql/handlers.md</c>.
/// </summary>
public class HandlerRenderingTests {
    static QueryBuilder Build(string sql) => new QueryCommand(sql).StartBuilder();


    [Fact]
    public void Number_handler_doc_example_offset_fetch() {
        var sql = "SELECT Name FROM products ORDER BY Id OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY";
        var b = Build(sql);
        b.Use("@Skip", 50);
        b.Use("@Take", 50);
        Render.Expect(b, "SELECT Name FROM products ORDER BY Id OFFSET 50 ROWS FETCH NEXT 50 ROWS ONLY");
        Render.Expect(Build(sql), "SELECT Name FROM products ORDER BY Id");
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(9L, "9")]
    [InlineData(1.5, "1.5")]
    [InlineData((short)7, "7")]
    public void Number_handler_writes_any_number_invariant(object value, string expected) {
        var b = Build("SELECT @V_N AS X");
        b.Use("@V", value);
        Render.Expect(b, $"SELECT {expected} AS X");
    }

    [Fact]
    public void Number_handler_writes_bools_and_enums_numerically() {
        var b = Build("SELECT @V_N AS X");
        b.Use("@V", true);
        Render.Expect(b, "SELECT 1 AS X");
        var b2 = Build("SELECT @V_N AS X");
        b2.Use("@V", false);
        Render.Expect(b2, "SELECT 0 AS X");
        var b3 = Build("SELECT @V_N AS X");
        b3.Use("@V", DayOfWeek.Wednesday);
        Render.Expect(b3, "SELECT 3 AS X");
    }


    [Fact]
    public void String_handler_quotes_the_value() {
        var b = Build("SELECT * FROM artists WHERE Name = @Name_S");
        b.Use("@Name", "Queen");
        Render.Expect(b, "SELECT * FROM artists WHERE Name = 'Queen'");
    }

    [Fact]
    public void String_handler_stringifies_a_non_string() {
        var b = Build("SELECT * FROM artists WHERE Name = @Name_S");
        b.Use("@Name", 12);
        Render.Expect(b, "SELECT * FROM artists WHERE Name = '12'");
    }

    class NullToString {
        public override string? ToString() => null;
    }

    [Fact]
    public void String_handler_treats_a_null_ToString_as_empty() {
        var b = Build("SELECT * FROM artists WHERE Name = @Name_S");
        b.Use("@Name", new NullToString());
        Render.Expect(b, "SELECT * FROM artists WHERE Name = ''");
    }


    [Fact]
    public void Raw_handler_writes_the_value_verbatim() {
        var b = Build("SELECT Id, Name FROM @Table_R WHERE IsActive = 1");
        b.Use("@Table", "tracks");
        Render.Expect(b, "SELECT Id, Name FROM tracks WHERE IsActive = 1");
    }


    [Fact]
    public void Missing_required_handler_value_throws_at_generation() {
        Assert.Throws<RequiredHandlerValueException>(() => Render.From(Build("SELECT * FROM t WHERE x = @V_N")));
        Assert.Throws<RequiredHandlerValueException>(() => Render.From(new QueryCommand("SELECT * FROM t WHERE x = @V_N"), new { }));
    }

    [Fact]
    public void NotSet_placeholder_throws_if_rendered() {
        var sb = new ValueStringBuilder(stackalloc char[8]);
        try {
            IQuerySegmentHandler.NotSet.Handle(ref sb, "");
            Assert.Fail("the NotSet placeholder must throw");
        }
        catch (NotImplementedException) { }
        finally {
            sb.Dispose();
        }
    }

    [Fact]
    public void An_unregistered_suffix_letter_is_rejected_at_construction() {
        Assert.ThrowsAny<Exception>(() => new QueryCommand("SELECT * FROM t WHERE x = @V_Q"));
    }


    class UpperHandler : IQuerySegmentHandler {
        public void Handle(ref ValueStringBuilder sb, object value) => sb.Append(value.ToString()!.ToUpperInvariant());
    }

    [Fact]
    public void A_custom_base_letter_renders_through_its_handler() {
        QueryFactory.BaseHandlerMapper['U'] = _ => new UpperHandler();
        try {
            var b = Build("SELECT * FROM t WHERE x = @V_U");
            b.Use("@V", "loud");
            Render.Expect(b, "SELECT * FROM t WHERE x = LOUD");
        }
        finally {
            QueryFactory.BaseHandlerMapper.Remove('U');
        }
    }


    [Fact]
    public void Spread_renders_numbered_parameters() {
        var b = Build("SELECT * FROM t WHERE Id IN (@Ids_X)");
        b.Use("@Ids", new[] { 7, 8, 9 });
        Render.Expect(b, "SELECT * FROM t WHERE Id IN (@Ids_1, @Ids_2, @Ids_3)", ("@Ids_1", 7), ("@Ids_2", 8), ("@Ids_3", 9));
    }

    [Fact]
    public void Spread_through_the_IDbCommand_interface_matches() {
        var b = Build("SELECT * FROM t WHERE Id IN (@Ids_X)");
        b.Use("@Ids", new List<int> { 7, 8 });
        var cmd = Render.FromInterface(b);
        Render.AssertCommand(cmd, "SELECT * FROM t WHERE Id IN (@Ids_1, @Ids_2)", ("@Ids_1", 7), ("@Ids_2", 8));
    }

    [Fact]
    public void Spread_of_a_non_generic_collection_counts_through_ICollection() {
        var b = Build("SELECT * FROM t WHERE Id IN (@Ids_X)");
        b.Use("@Ids", new ArrayList { 1, 2 });
        Render.Expect(b, "SELECT * FROM t WHERE Id IN (@Ids_1, @Ids_2)", ("@Ids_1", 1), ("@Ids_2", 2));
    }

    static IEnumerable<object> LazyItems() {
        yield return 5;
        yield return 6;
    }

    [Fact]
    public void Spread_of_a_lazy_enumerable_binds_and_renders() {
        var b = Build("SELECT * FROM t WHERE Id IN (@Ids_X)");
        b.Use("@Ids", LazyItems());
        Render.Expect(b, "SELECT * FROM t WHERE Id IN (@Ids_1, @Ids_2)", ("@Ids_1", 5), ("@Ids_2", 6));
    }

    [Fact]
    public void Empty_lazy_enumerable_counts_as_absent() {
        static IEnumerable<object> None() { yield break; }
        var b = Build("SELECT * FROM t WHERE IsActive = 1 &AND Id IN (?@Ids_X)");
        b.Use("@Ids", None());
        Render.Expect(b, "SELECT * FROM t");
    }

    [Fact]
    public void Empty_required_spread_counts_as_absent_and_throws() {
        var b = Build("SELECT * FROM t WHERE x IN (@A, @Ids_X)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());
        Assert.Throws<RequiredHandlerValueException>(() => Render.From(b));
    }

    [Fact]
    public void Empty_spread_in_a_list_trims_its_comma() {
        var b = Build("SELECT * FROM t WHERE x IN (@A, @Ids_X)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());
        var cmd = Render.FromInterface(b);
        Render.AssertCommand(cmd, "SELECT * FROM t WHERE x IN (@A)", ("@A", 1));
    }

    [Fact]
    public void Spread_of_ten_or_more_items_keeps_names_aligned() {
        var items = Enumerable.Range(1, 12).ToArray();
        var b = Build("SELECT * FROM t WHERE Id IN (@Ids_X)");
        b.Use("@Ids", items);
        var cmd = Render.From(b);
        Assert.Contains("@Ids_9, @Ids_10", cmd.CommandText);
        Assert.Equal(12, cmd.BoundParameters.Count);
        Assert.Equal("@Ids_12", cmd.BoundParameters[^1].ParameterName);
    }


    struct SpanArgs {
        public int Min { get; set; }
        public int[]? Ids { get; set; }
    }

    [Fact]
    public void A_struct_parameter_object_binds_and_renders() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND Id IN (?@Ids_X)");
        Render.Expect(query, new SpanArgs { Min = 4, Ids = [1, 2] },
            "SELECT * FROM t WHERE a > @Min AND Id IN (@Ids_1, @Ids_2)", ("@Min", 4), ("@Ids_1", 1), ("@Ids_2", 2));
        Render.Expect(query, new SpanArgs { Min = 4 }, "SELECT * FROM t WHERE a > @Min", ("@Min", 4));
    }

    [Fact]
    public void A_struct_parameter_object_through_the_ref_overloads() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min");
        var args = new SpanArgs { Min = 9 };
        var usage = new bool[query.Mapper.Count];

        var dbCmd = new FakeCommand();
        query.SetCommand((System.Data.Common.DbCommand)dbCmd, ref args, usage);
        Render.AssertCommand(dbCmd, "SELECT * FROM t WHERE a > @Min", ("@Min", 9));

        var iCmd = new FakeCommand();
        query.SetCommand((IDbCommand)iCmd, ref args, usage);
        Render.AssertCommand(iCmd, "SELECT * FROM t WHERE a > @Min", ("@Min", 9));
    }

    [Fact]
    public void A_struct_parameter_object_through_the_IDbCommand_interface() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min");
        var usage = new bool[query.Mapper.Count];
        var cmd = new FakeCommand();
        query.SetCommand((IDbCommand)cmd, new SpanArgs { Min = 6 }, usage);
        Render.AssertCommand(cmd, "SELECT * FROM t WHERE a > @Min", ("@Min", 6));
    }

    [Fact]
    public void A_class_parameter_object_through_the_IDbCommand_interface() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min AND Id IN (?@Ids_X)");
        var usage = new bool[query.Mapper.Count];
        var cmd = new FakeCommand();
        query.SetCommand((IDbCommand)cmd, new { Min = 2, Ids = new[] { 3 } }, usage);
        Render.AssertCommand(cmd, "SELECT * FROM t WHERE a > @Min AND Id IN (@Ids_1)", ("@Min", 2), ("@Ids_1", 3));
    }

    [Fact]
    public void The_explicit_command_interface_exposes_the_layout() {
        IQueryCommand query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b IN (?@B_X) AND c = @C_N /*Flag*/AND d = 1");
        Assert.Same(((QueryCommand)query).Mapper, query.Mapper);
        Assert.True(query.StartSpecialHandlers <= query.StartBaseHandlers);
        Assert.True(query.StartBaseHandlers <= query.StartBoolCond);
        Assert.Equal(query.Mapper.Count - 1, query.StartBoolCond);
    }

    [Fact]
    public void Concurrent_first_sight_of_a_parameter_type_is_safe() {
        var query = new QueryCommand("SELECT * FROM t WHERE a > ?@Min");
        using var barrier = new Barrier(4);
        Parallel.For(0, 4, _ => {
            barrier.SignalAndWait();
            var cmd = new FakeCommand();
            var usage = new bool[query.Mapper.Count];
            query.SetCommand(cmd, new SpanArgs { Min = 1 }, usage);
            Assert.Equal("SELECT * FROM t WHERE a > @Min", cmd.CommandText);
        });
    }

    [Fact]
    public void Parameter_ledger_exposes_its_infos_and_rejects_out_of_range_updates() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b IN (?@B_X)");
        Assert.Equal(1, query.Parameters.VariablesInfo.Length);
        Assert.Equal(1, query.Parameters.SpecialHandlers.Length);
        var info = SizedDbParamCache.Get(DbType.String, 64);
        Assert.False(query.UpdateParamCache("@Nope", info));
        Assert.False(query.UpdateParamCache("@B", info));
        Assert.True(query.UpdateParamCache("@A", info));
    }

    [Fact]
    public void A_template_longer_than_the_stack_buffer_renders_whole() {
        var wide = string.Join(", ", Enumerable.Range(0, 80).Select(i => $"Column_Number_{i:D4}"));
        var sql = $"SELECT {wide}, /*K*/Extra FROM t";
        var b = Build(sql);
        b.Use("K");
        Render.Expect(b, $"SELECT {wide}, Extra FROM t");
        Render.Expect(Build(sql), $"SELECT {wide} FROM t");
    }

    [Fact]
    public void Rendering_more_than_the_learning_window_stops_updating_the_average() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*K*/x = 1");
        var vars = new object?[query.QueryText.RequiredVariablesLength];
        for (int i = 0; i < 1100; i++)
            Assert.Equal("SELECT * FROM t", query.QueryText.Parse(vars));
        vars[query.Mapper.GetIndex("K")] = "on";
        Assert.Equal("SELECT * FROM t WHERE x = 1", query.QueryText.Parse(vars));
    }

    [Fact]
    public void An_untouched_template_returns_the_original_string_instance() {
        var query = new QueryCommand("SELECT * FROM t WHERE /*K*/x = 1");
        var vars = new object?[query.QueryText.RequiredVariablesLength];
        vars[query.Mapper.GetIndex("K")] = "on";
        Assert.Same(query.QueryText.QueryString, query.QueryText.Parse(vars));
    }
}
