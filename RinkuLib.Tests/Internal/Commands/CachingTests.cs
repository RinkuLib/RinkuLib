using System.Data.Common;
using System.Reflection;
using Rinku;
using Rinku.Mapping;
using Rinku.Querying;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// A command learns its row parser per condition shape and reuses it, so runs that render different
/// SQL never poison each other's cache.
/// </summary>
public class CachingTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    public record UserIdentity(long ID, string Name, string? Email = null);

    [Fact]
    public void Each_condition_shape_gets_its_own_parser() {
        var query = new QueryCommand("SELECT ID, /*Name*/Name FROM Users WHERE Name = ?@Name");
        using var cnn = Db.GetConnection();

        var oneColumn = query.StartBuilder();
        var row1 = oneColumn.Query<DynaObject>(cnn);
        Assert.NotNull(row1);
        Assert.Single(row1);
        Assert.Equal(["ID"], row1.Keys);

        var twoColumns = query.StartBuilder();
        twoColumns.Use("Name");
        twoColumns.Use("@Name", "Victor");
        var row2 = twoColumns.Query<DynaObject>(cnn);
        Assert.NotNull(row2);
        Assert.Equal(2, row2.Count);
        Assert.Equal(["ID", "Name"], row2.Keys);

        var filteredOneColumn = query.StartBuilder();
        filteredOneColumn.Use("@Name", "Victor");
        var row3 = filteredOneColumn.Query<DynaObject>(cnn);
        Assert.NotNull(row3);
        Assert.Single(row3);
        Assert.Equal(["ID"], row3.Keys);

        var twoColumnsNoFilter = query.StartBuilder();
        twoColumnsNoFilter.Use("Name");
        var row4 = twoColumnsNoFilter.Query<DynaObject>(cnn);
        Assert.NotNull(row4);
        Assert.Equal(2, row4.Count);
        Assert.Equal(["ID", "Name"], row4.Keys);
    }

    [Fact]
    public void Parser_cache_fills_after_the_first_run() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();

        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage[0] = true;
        Assert.False(query.TryGetCachedParser<UserRow>(usage, out _));

        query.Query<UserRow>(cnn, new { ID = 1 });

        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var parser));
        Assert.NotNull(parser);
    }

    [Fact]
    public void Warm_runs_reuse_the_same_parser_instance() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();
        query.Query<UserRow>(cnn, new { ID = 1 });

        Span<bool> usage = stackalloc bool[query.Mapper.Count];
        usage[0] = true;
        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var first));
        Assert.True(query.TryGetCachedParser<UserRow>(usage, out var second));
        Assert.Same(first, second);
    }

    [Fact]
    public void Cached_parser_still_returns_correct_results_on_reruns() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();
        for (int i = 1; i <= 3; i++) {
            var again = query.Query<UserRow>(cnn, new { ID = 2 });
            Assert.NotNull(again);
            Assert.Equal("Victor", again.Name);
        }
    }

    [Fact]
    public void CachedTypeParser_maps_a_self_built_command() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 1";
        var user = parser.Query((DbCommand)cmd);
        Assert.NotNull(user);
        Assert.Equal("John", user.Name);
    }

    [Fact]
    public void CachedTypeParser_reuses_its_parser_on_the_second_run() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 1";

        Assert.Equal(System.Data.CommandBehavior.SingleResult, parser.Behavior);
        parser.Query((DbCommand)cmd);
        var warm = parser.Query((DbCommand)cmd);
        Assert.NotNull(warm);
        Assert.Equal("John", warm.Name);
    }

    [Fact]
    public async Task CachedTypeParser_queries_async() {
        var parser = new CachedTypeParser<UserRow>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 2";
        var cold = await parser.QueryAsync((DbCommand)cmd, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(cold);
        Assert.Equal("Victor", cold.Name);
        var warm = await parser.QueryAsync((DbCommand)cmd, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(warm);
        Assert.Equal("Victor", warm.Name);
    }

    [Fact]
    public async Task CachedTypeParser_streams_rows() {
        var parser = new CachedTypeParser<string>();
        using var cnn = Db.Open();

        for (int pass = 0; pass < 2; pass++) {
            using var cmd = cnn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Users ORDER BY ID";
            var names = new List<string>();
            await foreach (var name in parser.StreamQueryAsync(cmd, disposeCommand: false, ct: TestContext.Current.CancellationToken))
                names.Add(name);
            Assert.Equal(["John", "Victor", "Alice"], names);
        }
    }

    [Fact]
    public void CachedTypeParser_works_through_the_IDbCommand_path() {
        var parser = new CachedTypeParser<long>();
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        Assert.Equal(3L, parser.Query((System.Data.IDbCommand)cmd));
        Assert.Equal(3L, parser.Query((System.Data.IDbCommand)cmd));
    }

    /// <summary>
    /// The cache is keyed on which keys a run supplies, and a handler's value is not one of them. Two runs
    /// supplying the same keys are one shape, so the parser learned for the first serves the second even
    /// when a <c>_R</c> value changed the columns. This is the limit
    /// <c>docs/articles/conditional-sql/handlers.md</c> names, one command per shape when the value decides
    /// what comes back.
    /// </summary>
    [Fact]
    public void A_handler_value_is_not_part_of_the_cache_key() {
        var query = new QueryCommand("SELECT @Cols_R FROM Users WHERE ID = 2");
        using var cnn = Db.Open();

        var first = query.StartBuilder();
        first.Use("@Cols", "ID, Name");
        Assert.Equal(["ID", "Name"], first.Query<DynaObject>(cnn).Keys.ToArray());

        var second = query.StartBuilder();
        second.Use("@Cols", "Salary, Email");
        Assert.Equal(["ID", "Name"], second.Query<DynaObject>(cnn).Keys.ToArray());
    }

    [Fact]
    public void Non_generic_CachedTypeParser_caches_each_requested_type_over_its_fixed_schema() {
        using var cache = CachedTypeParser.From<UserRow>();
        var userParser = cache.Get<UserRow>();
        Assert.Same(userParser, cache.Get<UserRow>());

        var identityParser = cache.Get<UserIdentity>();
        Assert.NotSame(userParser, identityParser);

        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "SELECT ID, Name, Email FROM Users WHERE ID = 1";
        Assert.Equal("John", userParser.Query(cmd).Name);
        Assert.Equal("John", identityParser.Query(cmd).Name);
    }

    [Fact]
    public void Non_generic_CachedTypeParser_accepts_every_schema_source() {
        using var type = new CachedTypeParser(typeof(UserRow));
        using var ctor = new CachedTypeParser(typeof(UserRow).GetConstructors()[0]);
        MethodInfo methodInfo = typeof(CachingTests).GetMethod(nameof(MakeUserIdentity))
            ?? throw new InvalidOperationException("The schema factory method was not found.");
        using var method = new CachedTypeParser(methodInfo);
        using var factory = new CachedTypeParser((Func<long, string, string?, UserIdentity>)MakeUserIdentity);
        using var generic = CachedTypeParser.From<UserRow>();

        Assert.NotNull(type.Get<UserIdentity>());
        Assert.NotNull(ctor.Get<UserIdentity>());
        Assert.NotNull(method.Get<UserIdentity>());
        Assert.NotNull(factory.Get<UserIdentity>());
        Assert.NotNull(generic.Get<UserIdentity>());
    }

    public static UserIdentity MakeUserIdentity(long id, string name, string? email) => new(id, name, email);

    /// <summary>
    /// A root dictionary is the schema-adaptive alternative for a controlled raw projection. Its cached row
    /// parser asks the current reader for names and values, so changing a handler value does not reuse stale
    /// ordinals or names.
    /// </summary>
    [Fact]
    public void A_dictionary_adapts_when_a_raw_handler_changes_the_projection() {
        var query = new QueryCommand("SELECT @Cols_R FROM Users WHERE ID = 2");
        using var cnn = Db.Open();

        var first = query.StartBuilder();
        first.Use("@Cols", "ID, Name");
        var identity = first.Query<Dictionary<string, object>>(cnn);
        Assert.Equal(["ID", "Name"], identity.Keys);
        Assert.Equal(2L, identity["ID"]);
        Assert.Equal("Victor", identity["Name"]);

        var second = query.StartBuilder();
        second.Use("@Cols", "Salary, Email");
        var contact = second.Query<Dictionary<string, object>>(cnn);
        Assert.Equal(["Salary", "Email"], contact.Keys);
        Assert.Equal(20.0, contact["Salary"]);
        Assert.Equal("victor@corp.com", contact["Email"]);
    }

    [Fact]
    public void A_dictionary_deduplicates_runtime_column_names() {
        var query = new QueryCommand("SELECT ID AS Value, Name AS Value FROM Users WHERE ID = 1");
        using var cnn = Db.Open();

        var row = query.Query<Dictionary<string, object>>(cnn);

        Assert.Equal(["Value", "Value#2"], row.Keys);
        Assert.Equal(1L, row["value"]);
        Assert.Equal("John", row["VALUE#2"]);
    }

    [Fact]
    public void A_list_of_dictionaries_adapts_every_row_after_a_raw_projection_changes() {
        var query = new QueryCommand("SELECT @Cols_R FROM Users ORDER BY ID");
        using var cnn = Db.Open();

        var first = query.StartBuilder();
        first.Use("@Cols", "ID, Name");
        var identities = first.Query<List<Dictionary<string, object>>>(cnn);
        Assert.Equal(3, identities.Count);
        Assert.Equal(["ID", "Name"], identities[0].Keys);
        Assert.Equal("Alice", identities[2]["Name"]);

        var second = query.StartBuilder();
        second.Use("@Cols", "Email, Salary");
        var contacts = second.Query<List<Dictionary<string, object>>>(cnn);
        Assert.Equal(3, contacts.Count);
        Assert.Equal(["Email", "Salary"], contacts[0].Keys);
        Assert.Null(contacts[0]["Email"]);
        Assert.Equal(20.0, contacts[1]["Salary"]);
    }

    /// <summary>
    /// A command read through a <see cref="MultiReader"/> stores a parser per result set. Running it on its
    /// own afterwards has to take the first set's, so the lookup skips an entry belonging to another set
    /// rather than stopping at it.
    /// </summary>
    [Fact]
    public void A_later_result_sets_parser_is_not_used_for_the_first_set() {
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID = 1; SELECT Name FROM Users WHERE ID = 1");
        using var cnn = Db.Open();

        using (var multi = query.ExecuteMultiReader(cnn, out var cmd))
        using (cmd) {
            Assert.Equal("ID", multi.Query<DynaObject>().Keys.ToArray()[0]);
            Assert.Equal("Name", multi.Query<DynaObject>().Keys.ToArray()[0]);
        }

        Assert.Equal("ID", query.StartBuilder().Query<DynaObject>(cnn).Keys.ToArray()[0]);
        Assert.Equal("ID", query.Query<DynaObject>(cnn).Keys.ToArray()[0]);
    }

    /// <summary>Each key combination is its own shape, so a marker-driven projection needs no second command.</summary>
    [Fact]
    public void A_marker_driven_projection_gets_a_parser_per_combination() {
        var query = new QueryCommand("?SELECT ID, Name, Email FROM Users WHERE ID = 2");
        using var cnn = Db.Open();

        var ids = query.StartBuilder();
        ids.Use("ID");
        Assert.Equal(["ID"], ids.Query<DynaObject>(cnn).Keys.ToArray());

        var names = query.StartBuilder();
        names.Use("Name");
        Assert.Equal(["Name"], names.Query<DynaObject>(cnn).Keys.ToArray());

        var both = query.StartBuilder();
        both.Use("ID");
        both.Use("Email");
        Assert.Equal(["ID", "Email"], both.Query<DynaObject>(cnn).Keys.ToArray());
    }

    public record RacedRow(int Id, string Name) : IDbReadable;

    /// <summary>
    /// The shared parser store is read without a lock and written under one, so several threads meeting an
    /// unseen shape at once all reach the write. The one that gets there first records the parser and the
    /// rest have to find it on their second look rather than each recording one of their own.
    /// </summary>
    [Fact]
    public void First_sight_of_a_shape_from_several_threads_yields_one_parser() {
        ColumnInfo[] shape = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        const int threads = 8;
        var parsers = new ITypeParser<RacedRow>[threads];
        using var barrier = new Barrier(threads);

        Parallel.For(0, threads, i => {
            var cols = (ColumnInfo[])shape.Clone();
            barrier.SignalAndWait();
            parsers[i] = TypeParser.GetTypeParser<RacedRow>(cols);
        });

        Assert.All(parsers, p => Assert.Same(parsers[0], p));
    }
}
