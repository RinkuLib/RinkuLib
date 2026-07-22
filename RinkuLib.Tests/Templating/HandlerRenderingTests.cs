using System.Collections;
using System.Data;
using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Templating;

/// <summary>
/// Rendering a template into the SQL a run sends: the base handlers (<c>_S</c>, <c>_R</c>, <c>_N</c>), the
/// spread (<c>_X</c>) with every value shape, custom letters, and the error paths. Expected output follows
/// <c>docs/articles/conditional-sql/handlers.md</c>.
/// </summary>
public class HandlerRenderingTests {
    static QueryBuilder Build(string sql) => new QueryCommand(sql).StartBuilder();

    /// <summary>
    /// A suffix letter resolves whatever its case, so <c>_x</c> spreads exactly as <c>_X</c> does. The
    /// suffix is consumed either way, leaving the variable under its bare name.
    /// </summary>
    [Theory]
    [InlineData("SELECT * FROM t WHERE a IN (@ids_X)")]
    [InlineData("SELECT * FROM t WHERE a IN (@ids_x)")]
    public void A_suffix_resolves_whatever_its_case(string sql) {
        var b = Build(sql);
        b.Use("@ids", new[] { 7, 8 });
        Assert.Equal("SELECT * FROM t WHERE a IN (@ids_1, @ids_2)", Render.From(b).CommandText);
    }

    [Theory]
    [InlineData("SELECT * FROM t WHERE a = @v_N", "SELECT * FROM t WHERE a = 46")]
    [InlineData("SELECT * FROM t WHERE a = @v_n", "SELECT * FROM t WHERE a = 46")]
    public void A_base_handler_suffix_resolves_whatever_its_case(string sql, string rendered) {
        var b = Build(sql);
        b.Use("@v", 46);
        Assert.Equal(rendered, Render.From(b).CommandText);
    }


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

    /// <summary>
    /// The handler writes a literal, so a quote in the value is doubled and the literal still holds the
    /// whole value. Without that the value could close the literal and the rest of it would be read as SQL.
    /// </summary>
    [Theory]
    [InlineData("O'Brien", "'O''Brien'")]
    [InlineData("'", "''''")]
    [InlineData("''", "''''''")]
    [InlineData("a'b'c", "'a''b''c'")]
    [InlineData("x'; DROP TABLE artists; --", "'x''; DROP TABLE artists; --'")]
    public void String_handler_doubles_a_quote_in_the_value(string value, string expected) {
        var b = Build("SELECT * FROM artists WHERE Name = @Name_S");
        b.Use("@Name", value);
        Render.Expect(b, "SELECT * FROM artists WHERE Name = " + expected);
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
        catch (RinkuException e) when (e.Code == ErrorCodes.InternalInvariant) { }
        finally {
            sb.Dispose();
        }
    }

    /// <summary>
    /// An unregistered suffix letter is a typo in the SQL, so the rejection names the suffix and the
    /// variable it sat on rather than surfacing the bare lookup failure behind it.
    /// </summary>
    [Fact]
    public void An_unregistered_suffix_letter_is_rejected_at_construction() {
        var ex = Assert.ThrowsAny<Exception>(() => new QueryCommand("SELECT * FROM t WHERE x = @V_Q"));
        Assert.IsNotType<KeyNotFoundException>(ex);
        Assert.Contains("_Q", ex.Message);
        Assert.Contains("@V", ex.Message);
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

    /// <summary>
    /// An empty collection counts as absent, and a required spread has nothing to render without one, so
    /// the refusal comes while the SQL is built. Both command roads answer the same way: the overload a
    /// caller happens to reach for cannot decide whether a required value may be skipped.
    /// </summary>
    [Fact]
    public void Empty_required_spread_counts_as_absent_and_throws() {
        var b = Build("SELECT * FROM t WHERE x IN (@A, @Ids_X)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());
        Assert.Throws<RequiredHandlerValueException>(() => Render.From(b));

        var viaInterface = Build("SELECT * FROM t WHERE x IN (@A, @Ids_X)");
        viaInterface.Use("@A", 1);
        viaInterface.Use("@Ids", Array.Empty<int>());
        Assert.Throws<RequiredHandlerValueException>(() => Render.FromInterface(viaInterface));
    }

    /// <summary>
    /// A marker holds the footprint to the spread rather than growing out of the parenthesis, so an empty
    /// collection drops the spread and the comma binding it, leaving the entry beside it standing.
    /// </summary>
    [Fact]
    public void Empty_marked_spread_in_a_list_trims_its_comma() {
        var b = Build("SELECT * FROM t WHERE x IN (@A, /*@Ids*/@Ids_X)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());
        Render.AssertCommand(Render.From(b), "SELECT * FROM t WHERE x IN (@A)", ("@A", 1));

        var viaInterface = Build("SELECT * FROM t WHERE x IN (@A, /*@Ids*/@Ids_X)");
        viaInterface.Use("@A", 1);
        viaInterface.Use("@Ids", Array.Empty<int>());
        Render.AssertCommand(Render.FromInterface(viaInterface), "SELECT * FROM t WHERE x IN (@A)", ("@A", 1));
    }

    /// <summary>
    /// The connector after a footprint belongs to it, so the first entry of a list takes its own comma
    /// when it goes. Left behind, the comma opens the list on nothing and the statement will not parse.
    /// </summary>
    /// <summary>
    /// The first entry of a list owns the comma after it, so an empty spread there leaves the list opening on
    /// the entry that follows. The space between that comma and the next entry was never inside the footprint
    /// and no pass rewrites whitespace, so it stays where it was written.
    /// </summary>
    [Fact]
    public void An_empty_spread_opening_a_list_takes_its_comma_with_it() {
        var b = Build("SELECT * FROM t WHERE x IN (/*@Ids*/@Ids_X, @A)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());
        Render.AssertCommand(Render.From(b), "SELECT * FROM t WHERE x IN ( @A)", ("@A", 1));
    }

    /// <summary>
    /// <c>GetQueryText</c> reports the SQL the current state would produce, so it has to agree with the
    /// SQL that state binds. An empty collection counts as absent, and reading the text first cannot
    /// change that: the emptiness is a property of the value, not something a bind discovers.
    /// </summary>
    [Fact]
    public void The_debug_text_agrees_with_the_bound_sql_for_an_empty_spread() {
        var b = Build("SELECT * FROM t WHERE x IN (@A, ?@Ids_X)");
        b.Use("@A", 1);
        b.Use("@Ids", Array.Empty<int>());

        var textFirst = b.GetQueryText();
        Assert.Equal(Render.From(b).CommandText, textFirst);
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


    class SpreadShapeArgs {
        public int A { get; set; }
        public object? Ids { get; set; }
    }

    /// <summary>Counts how many times something walks it, and can be told to allow only one walk.</summary>
    sealed class WatchedSequence(int count, bool oneShot = false, bool grows = false) : System.Collections.IEnumerable {
        public int Walks;
        private int Length = count;
        public System.Collections.IEnumerator GetEnumerator() {
            if (oneShot && Walks > 0)
                throw new InvalidOperationException("this sequence can only be walked once");
            Walks++;
            var take = grows ? Length++ : Length;
            return Items(take);
        }
        private static System.Collections.IEnumerator Items(int take) {
            for (int i = 1; i <= take; i++)
                yield return i;
        }
    }

    private static readonly QueryCommand SpreadOnly = new("SELECT * FROM t WHERE Id IN (@Ids_X)");

    /// <summary>
    /// A spread needs a count and the bind already took one, so the source is read once and no more. Reading
    /// it again is not free and not safe, since a sequence is under no obligation to answer the same way
    /// twice.
    /// </summary>
    /// <remarks>
    /// FAILING. The parameter-object road reads it twice. The bind rewrites its slot to the count, but that
    /// slot is a local in <c>ActualSetCommand</c>, and the render then fetches the member again through the
    /// accessor and walks the original to recount. The other roads keep the rewrite because their slot is
    /// the values array, so they read once.
    /// </remarks>
    [Fact]
    public void A_spread_reads_its_source_once() {
        var viaBuilder = new WatchedSequence(3);
        var b = SpreadOnly.StartBuilder();
        b.Use("@Ids", viaBuilder);
        Render.From(b);
        Assert.Equal(1, viaBuilder.Walks);

        var viaObject = new WatchedSequence(3);
        Render.From(SpreadOnly, new SpreadShapeArgs { A = 1, Ids = viaObject });
        Assert.Equal(1, viaObject.Walks);
    }

    /// <summary>
    /// A sequence that refuses a second walk is a sequence, and one walk is all a spread ever needs.
    /// </summary>
    /// <remarks>FAILING, for the reason on <see cref="A_spread_reads_its_source_once"/>.</remarks>
    [Fact]
    public void A_one_shot_sequence_binds_through_a_parameter_object() {
        var cmd = Render.From(SpreadOnly, new SpreadShapeArgs { A = 1, Ids = new WatchedSequence(2, oneShot: true) });
        Render.AssertCommand(cmd, "SELECT * FROM t WHERE Id IN (@Ids_1, @Ids_2)", ("@Ids_1", 1), ("@Ids_2", 2));
    }

    /// <summary>
    /// The SQL names one parameter per element and the bind adds them, so the two counts are the same count
    /// and cannot disagree. A source that answers differently on a second walk is only able to split them if
    /// something walked it twice.
    /// </summary>
    /// <remarks>
    /// FAILING, and the quiet one. The others throw, this one hands the provider a statement naming a
    /// parameter that was never bound.
    /// </remarks>
    [Fact]
    public void The_rendered_parameters_are_the_ones_that_were_bound() {
        var cmd = Render.From(SpreadOnly, new SpreadShapeArgs { A = 1, Ids = new WatchedSequence(2, grows: true) });
        var named = cmd.CommandText.Count(c => c == '@');
        Assert.Equal(cmd.BoundParameters.Count, named);
    }

    class OverriddenSpreadArgs {
        public int A { get; set; }
        [NotDefault] public int[]? Ids { get; set; }
    }

    /// <summary>
    /// A presence rule the caller declares decides whether the value travels at all, and this one sends an
    /// empty collection through where the plain rule would have stopped it for being the type's default. What
    /// it does not decide is what the spread makes of it. The value arrives, the handler reads it, and its
    /// answer settles the footprint the same as anywhere else.
    /// </summary>
    [Fact]
    public void A_presence_rule_decides_what_travels_and_the_handler_decides_the_rest() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b IN (?@Ids_X)");

        Render.AssertCommand(Render.From(query, new OverriddenSpreadArgs { A = 1, Ids = [] }),
            "SELECT * FROM t WHERE a = @A", ("@A", 1));

        Render.AssertCommand(Render.From(query, new OverriddenSpreadArgs { A = 1, Ids = [7, 8] }),
            "SELECT * FROM t WHERE a = @A AND b IN (@Ids_1, @Ids_2)", ("@A", 1), ("@Ids_1", 7), ("@Ids_2", 8));
    }

    /// <summary>
    /// The same value on a spread the query requires. The handler still has the last word on what it can
    /// render, and a segment the template keeps regardless is left with nothing, which is refused while the
    /// SQL is built rather than sent as an empty pair of parentheses.
    /// </summary>
    [Fact]
    public void A_required_spread_the_handler_cannot_render_is_refused() {
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b IN (@Ids_X)");
        Refusals.Raises(ErrorCodes.RequiredHandlerValue,
            () => Render.From(query, new OverriddenSpreadArgs { A = 1, Ids = [] }));
    }

    /// <summary>
    /// The spread's reading of a collection reaches the engine two ways, asked of the handler when the values
    /// arrive loose and compiled into the presence check when they arrive on an object. The second is the
    /// first arrived at earlier, so whatever shape the collection takes, both roads have to render the same
    /// SQL. A rule that grew apart from its compiled twin would show up here first.
    /// </summary>
    [Fact]
    public void Both_roads_read_a_spread_the_same_way() {
        static IEnumerable<object> Lazy(params int[] items) {
            foreach (var i in items)
                yield return i;
        }
        (string Name, Func<object?> Value)[] shapes = [
            ("null", () => null),
            ("empty array", Array.Empty<int>),
            ("empty list", () => new List<int>()),
            ("empty lazy", () => Lazy()),
            ("one item", () => new[] { 7 }),
            ("three items", () => new[] { 7, 8, 9 }),
            ("lazy items", () => Lazy(7, 8)),
        ];
        var query = new QueryCommand("SELECT * FROM t WHERE a = ?@A AND b IN (?@Ids_X)");

        var loose = shapes.Select(s => {
            var b = query.StartBuilder();
            b.Use("@A", 1);
            b.Use("@Ids", s.Value());
            return $"{s.Name}: {Render.From(b).CommandText}";
        }).ToArray();

        var onAnObject = shapes.Select(s =>
            $"{s.Name}: {Render.From(query, new SpreadShapeArgs { A = 1, Ids = s.Value() }).CommandText}").ToArray();

        Assert.Equal(loose, onAnObject);
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
