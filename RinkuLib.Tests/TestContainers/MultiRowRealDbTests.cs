using Microsoft.Data.SqlClient;
using Npgsql;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

/// <summary>The shapes the real-database multi-row tests group into. Leaf elements are scalar records, so one list equality checks every field of every element.</summary>
public sealed record MrChild([InvalidOnNull] int Id, string Value) : IDbReadable;
public sealed record MrParent([property: GroupKey] int Id, string Name, List<MrChild> Children) : IDbReadable;
public sealed record MrArrayParent([property: GroupKey] int Id, string Name, MrChild[] Children) : IDbReadable;
public sealed record MrGrand([InvalidOnNull] int Id, string Data) : IDbReadable;
public sealed record MrNChild([property: GroupKey] int Id, string Value, List<MrGrand> Grands) : IDbReadable;
public sealed record MrNParent([property: GroupKey] int Id, string Name, List<MrNChild> Children) : IDbReadable;

/// <summary>Groups by a <c>[GroupKey]</c> method reading a real column: the Id drives grouping but is not a member.</summary>
public sealed record MrMethodParent(string Name, List<MrChild> Children) : IDbReadable {
    [GroupKey]
    public static (bool Same, int Next) SameParent(int stored, int id) => (id == stored, id);
}

/// <summary>
/// The multi-row feature against SQL Server: a real LEFT JOIN result grouped into nested objects, with a
/// childless parent and a childless middle level from the join, the sync and async roads, and a wrapper.
/// </summary>
public class MultiRowDbFixture : DBFixture<SqlConnection> {
    public override async ValueTask InitializeAsync() {
        await base.InitializeAsync();
        using var cnn = GetConnection();
        await cnn.OpenAsync();
        await new QueryCommand("""
            DROP TABLE IF EXISTS GrandChildren;
            DROP TABLE IF EXISTS Children;
            DROP TABLE IF EXISTS Parents;
            CREATE TABLE Parents (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NOT NULL);
            CREATE TABLE Children (Id INT NOT NULL PRIMARY KEY, ParentId INT NOT NULL, Val NVARCHAR(50) NOT NULL);
            CREATE TABLE GrandChildren (Id INT NOT NULL PRIMARY KEY, ChildId INT NOT NULL, Data NVARCHAR(50) NOT NULL);
            INSERT INTO Parents (Id, Name) VALUES (1,'P1'),(2,'P2'),(3,'P3');
            INSERT INTO Children (Id, ParentId, Val) VALUES (10,1,'c10'),(11,1,'c11'),(20,2,'c20');
            INSERT INTO GrandChildren (Id, ChildId, Data) VALUES (100,10,'g100'),(101,10,'g101'),(110,11,'g110');
            """).ExecuteAsync(cnn);
    }
}

public class MultiRowRealDbTests(MultiRowDbFixture fixture) : IClassFixture<MultiRowDbFixture> {
    private readonly MultiRowDbFixture Fixture = fixture;

    private static readonly QueryCommand Flat = new("""
        SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
        FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id
        ORDER BY p.Id, c.Id
        """);
    private static readonly QueryCommand Nested = new("""
        SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue,
               g.Id AS ChildrenGrandsId, g.Data AS ChildrenGrandsData
        FROM Parents p
        LEFT JOIN Children c ON c.ParentId = p.Id
        LEFT JOIN GrandChildren g ON g.ChildId = c.Id
        ORDER BY p.Id, c.Id, g.Id
        """);

    private static void AssertFlat(List<MrParent> parents) {
        Assert.Equal(3, parents.Count);
        Assert.Equal((1, "P1"), (parents[0].Id, parents[0].Name));
        Assert.Equal([new MrChild(10, "c10"), new MrChild(11, "c11")], parents[0].Children);
        Assert.Equal((2, "P2"), (parents[1].Id, parents[1].Name));
        Assert.Equal([new MrChild(20, "c20")], parents[1].Children);
        Assert.Equal((3, "P3"), (parents[2].Id, parents[2].Name));
        Assert.Empty(parents[2].Children);
    }

    [Fact]
    public async Task A_left_join_groups_children_and_leaves_a_childless_parent_empty() {
        using var cnn = Fixture.GetConnection();
        AssertFlat((await Flat.QueryAsync<List<MrParent>>(cnn, ct: TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public void The_synchronous_road_groups_the_same_result() {
        using var cnn = Fixture.GetConnection();
        AssertFlat(Flat.Query<List<MrParent>>(cnn)!);
    }

    [Fact]
    public async Task A_left_join_maps_a_built_in_array_member() {
        using var cnn = Fixture.GetConnection();
        var parents = await Flat.QueryAsync<List<MrArrayParent>>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, parents!.Count);
        Assert.IsType<MrChild[]>(parents[0].Children);
        Assert.Equal((1, "P1"), (parents[0].Id, parents[0].Name));
        Assert.Equal([new MrChild(10, "c10"), new MrChild(11, "c11")], parents[0].Children);
        Assert.Equal((2, "P2"), (parents[1].Id, parents[1].Name));
        Assert.Equal([new MrChild(20, "c20")], parents[1].Children);
        Assert.Equal((3, "P3"), (parents[2].Id, parents[2].Name));
        Assert.Empty(parents[2].Children);
    }

    [Fact]
    public async Task A_two_join_result_nests_three_levels_with_the_cascade() {
        using var cnn = Fixture.GetConnection();
        var parents = await Nested.QueryAsync<List<MrNParent>>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, parents!.Count);

        Assert.Equal((1, "P1"), (parents[0].Id, parents[0].Name));
        Assert.Equal(2, parents[0].Children.Count);
        Assert.Equal((10, "c10"), (parents[0].Children[0].Id, parents[0].Children[0].Value));
        Assert.Equal([new MrGrand(100, "g100"), new MrGrand(101, "g101")], parents[0].Children[0].Grands);
        Assert.Equal((11, "c11"), (parents[0].Children[1].Id, parents[0].Children[1].Value));
        Assert.Equal([new MrGrand(110, "g110")], parents[0].Children[1].Grands);

        Assert.Equal((2, "P2"), (parents[1].Id, parents[1].Name));
        Assert.Single(parents[1].Children);
        Assert.Equal((20, "c20"), (parents[1].Children[0].Id, parents[1].Children[0].Value));
        Assert.Empty(parents[1].Children[0].Grands);

        Assert.Equal((3, "P3"), (parents[2].Id, parents[2].Name));
        Assert.Empty(parents[2].Children);
    }

    [Fact]
    public async Task A_method_boundary_groups_a_real_left_join_by_a_computed_key() {
        using var cnn = Fixture.GetConnection();
        var parents = await Flat.QueryAsync<List<MrMethodParent>>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, parents!.Count);
        Assert.Equal("P1", parents[0].Name);
        Assert.Equal([new MrChild(10, "c10"), new MrChild(11, "c11")], parents[0].Children);
        Assert.Equal("P2", parents[1].Name);
        Assert.Equal([new MrChild(20, "c20")], parents[1].Children);
        Assert.Equal("P3", parents[2].Name);
        Assert.Empty(parents[2].Children);
    }

    [Fact]
    public void A_multi_reader_reads_a_spanning_set_then_the_next() {
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("""
            SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
            FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id ORDER BY p.Id, c.Id;
            SELECT Id, Name FROM Parents ORDER BY Id
            """);
        using var multi = q.ExecuteMultiReader(cnn, out var cmd);
        using (cmd) {
            var parents = multi.Query<List<MrParent>>();
            var idNames = multi.Query<List<(int, string)>>();

            AssertFlat(parents);
            Assert.Equal([(1, "P1"), (2, "P2"), (3, "P3")], idNames);
        }
    }

    [Fact]
    public void A_multi_reader_reads_spanning_groups_one_parse_at_a_time() {
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("""
            SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
            FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id ORDER BY p.Id, c.Id
            """);
        using var multi = q.ExecuteMultiReader(cnn, out var cmd);
        using (cmd) {
            var parents = new List<MrParent>();
            var reading = multi.Read();
            while (reading) {
                (reading, var parent) = multi.Get<MrParent>();
                parents.Add(parent);
            }
            AssertFlat(parents);
        }
    }

    [Fact]
    public async Task A_multi_reader_reads_a_spanning_set_then_the_next_async() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("""
            SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
            FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id ORDER BY p.Id, c.Id;
            SELECT Id, Name FROM Parents ORDER BY Id
            """);
        using var multi = await q.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct);
        using (cmd) {
            var parents = await multi.QueryAsync<List<MrParent>>(ct);
            var idNames = await multi.QueryAsync<List<(int, string)>>(ct);

            AssertFlat(parents);
            Assert.Equal([(1, "P1"), (2, "P2"), (3, "P3")], idNames);
        }
    }

    [Fact]
    public async Task A_multi_reader_reads_spanning_groups_one_parse_at_a_time_async() {
        var ct = TestContext.Current.CancellationToken;
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("""
            SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
            FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id ORDER BY p.Id, c.Id
            """);
        using var multi = await q.ExecuteMultiReaderAsync(cnn, out var cmd, ct: ct);
        using (cmd) {
            var parents = new List<MrParent>();
            var reading = await multi.ReadAsync(ct);
            while (reading) {
                (reading, var parent) = await multi.GetAsync<MrParent>(ct);
                parents.Add(parent);
            }
            AssertFlat(parents);
        }
    }

    [Fact]
    public async Task A_tuple_of_two_collections_reads_the_whole_result() {
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("SELECT Id, Name FROM Parents ORDER BY Id");
        var (ids, names) = await q.QueryAsync<(List<int>, List<string>)>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], ids);
        Assert.Equal(["P1", "P2", "P3"], names);
    }

    [Fact]
    public async Task A_wrapper_composes_over_the_real_multi_row_result() {
        using var cnn = Fixture.GetConnection();
        var q = new QueryCommand("""
            SELECT p.Id, p.Name, c.Id AS ChildrenId, c.Val AS ChildrenValue
            FROM Parents p LEFT JOIN Children c ON c.ParentId = p.Id
            WHERE p.Id = 1 ORDER BY c.Id
            """);
        MrParent single = await q.QueryAsync<Single<MrParent>>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal((1, "P1"), (single.Id, single.Name));
        Assert.Equal([new MrChild(10, "c10"), new MrChild(11, "c11")], single.Children);
    }
}

/// <summary>
/// The same feature against PostgreSQL, a different reader whose unquoted aliases fold to lower case, so this
/// also confirms the grouping key and prefixed collection columns match case-insensitively on a real join.
/// </summary>
public class MultiRowPostgresFixture : DBFixture<NpgsqlConnection> {
    public override async ValueTask InitializeAsync() {
        await base.InitializeAsync();
        using var cnn = GetConnection();
        await cnn.OpenAsync();
        await new QueryCommand("""
            DROP TABLE IF EXISTS children;
            DROP TABLE IF EXISTS parents;
            CREATE TABLE parents (id INT NOT NULL PRIMARY KEY, name VARCHAR(50) NOT NULL);
            CREATE TABLE children (id INT NOT NULL PRIMARY KEY, parentid INT NOT NULL, val VARCHAR(50) NOT NULL);
            INSERT INTO parents (id, name) VALUES (1,'P1'),(2,'P2'),(3,'P3');
            INSERT INTO children (id, parentid, val) VALUES (10,1,'c10'),(11,1,'c11'),(20,2,'c20');
            """).ExecuteAsync(cnn);
    }
}

public class MultiRowPostgresTests(MultiRowPostgresFixture fixture) : IClassFixture<MultiRowPostgresFixture> {
    private readonly MultiRowPostgresFixture Fixture = fixture;

    private static readonly QueryCommand Flat = new("""
        SELECT p.id, p.name, c.id AS ChildrenId, c.val AS ChildrenValue
        FROM parents p LEFT JOIN children c ON c.parentid = p.id
        ORDER BY p.id, c.id
        """);

    [Fact]
    public async Task A_postgres_left_join_groups_children_case_insensitively() {
        using var cnn = Fixture.GetConnection();
        var parents = await Flat.QueryAsync<List<MrParent>>(cnn, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, parents!.Count);
        Assert.Equal((1, "P1"), (parents[0].Id, parents[0].Name));
        Assert.Equal([new MrChild(10, "c10"), new MrChild(11, "c11")], parents[0].Children);
        Assert.Equal((2, "P2"), (parents[1].Id, parents[1].Name));
        Assert.Equal([new MrChild(20, "c20")], parents[1].Children);
        Assert.Equal((3, "P3"), (parents[2].Id, parents[2].Name));
        Assert.Empty(parents[2].Children);
    }
}
