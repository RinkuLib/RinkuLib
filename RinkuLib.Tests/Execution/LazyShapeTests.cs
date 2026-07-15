using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// The lazy and buffered result shapes over real readers: <c>IEnumerable&lt;T&gt;</c> streams while
/// iterated and owns the reader's lifetime, <c>List&lt;T&gt;</c> buffers everything up front.
/// </summary>
public class LazyShapeTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand Names = new("SELECT Name FROM Users ORDER BY ID");
    private static readonly QueryCommand UsersQuery = new("SELECT ID, Name, Email FROM Users ORDER BY ID");
    private static readonly QueryCommand NoRows = new("SELECT Name FROM Users WHERE 1 = 0");

    [Fact]
    public void Lazy_enumerable_of_scalars_yields_all_rows() {
        using var cnn = Db.GetConnection();
        var names = Names.Query<IEnumerable<string>>(cnn);
        Assert.NotNull(names);
        Assert.Equal(["John", "Victor", "Alice"], names.ToList());
    }

    [Fact]
    public void Lazy_enumerable_of_objects_yields_all_rows() {
        using var cnn = Db.GetConnection();
        var users = UsersQuery.Query<IEnumerable<UserRow>>(cnn);
        Assert.NotNull(users);
        Assert.Equal([1L, 2L, 3L], users.Select(u => u.ID).ToList());
    }

    [Fact]
    public void Lazy_enumerable_with_no_rows_is_empty() {
        using var cnn = Db.GetConnection();
        var names = NoRows.Query<IEnumerable<string>>(cnn);
        Assert.NotNull(names);
        Assert.Empty(names.ToList());
    }

    [Fact]
    public void Lazy_enumerable_supports_early_break() {
        using var cnn = Db.GetConnection();
        var names = Names.Query<IEnumerable<string>>(cnn);
        Assert.NotNull(names);
        foreach (var name in names) {
            Assert.Equal("John", name);
            break;
        }
    }

    [Fact]
    public async Task Lazy_enumerable_from_the_async_path() {
        using var cnn = Db.GetConnection();
        var names = await Names.QueryAsync<IEnumerable<string>>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(names);
        Assert.Equal(["John", "Victor", "Alice"], names.ToList());
    }

    [Fact]
    public async Task Buffered_list_of_objects_from_the_async_path() {
        using var cnn = Db.GetConnection();
        var users = await UsersQuery.QueryAsync<List<UserRow>>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(users);
        Assert.Equal(3, users.Count);
    }

    [Fact]
    public async Task Optional_of_an_object_from_the_async_path() {
        using var cnn = Db.GetConnection();
        var missing = await new QueryCommand("SELECT ID, Name, Email FROM Users WHERE 1 = 0")
            .QueryAsync<Optional<UserRow>>(cnn, ct: TestContext.Current.CancellationToken);
        UserRow? value = missing;
        Assert.Null(value);
    }

    [Fact]
    public async Task Single_of_an_object_from_the_async_path() {
        using var cnn = Db.GetConnection();
        var one = await new QueryCommand("SELECT ID, Name, Email FROM Users WHERE ID = 1")
            .QueryAsync<Single<UserRow>>(cnn, ct: TestContext.Current.CancellationToken);
        UserRow value = one;
        Assert.Equal("John", value.Name);
    }

    [Fact]
    public void List_of_tuples_buffers_all_rows() {
        using var cnn = Db.GetConnection();
        var rows = new QueryCommand("SELECT ID, Name FROM Users ORDER BY ID").Query<List<(long, string)>>(cnn);
        Assert.NotNull(rows);
        Assert.Equal([(1L, "John"), (2L, "Victor"), (3L, "Alice")], rows);
    }

    [Fact]
    public async Task Streamed_complex_rows_use_the_composed_parser() {
        using var cnn = Db.GetConnection();
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users ORDER BY ID");
        var seen = new List<string>();
        await foreach (var user in query.StreamQueryAsync<NamedUser>(cnn, ct: TestContext.Current.CancellationToken))
            seen.Add(user.Username);
        Assert.Equal(["John", "Victor", "Alice"], seen);
    }
}
