using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.GettingStarted;

/// <summary>Executable examples for <c>docs/articles/getting-started/quick-start.md</c>.</summary>
public class QuickStartDocumentationTests(SqliteDb db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand GetUsers = new("SELECT ID, Name, Email FROM Users ORDER BY ID");
    private static readonly QueryCommand GetUserById = new("SELECT ID, Name, Email FROM Users WHERE ID = @id");
    private static readonly QueryCommand CountUsers = new("SELECT COUNT(*) FROM Users");
    private static readonly QueryCommand RenameUser = new("UPDATE Users SET Name = @name WHERE ID = @id");
    private static readonly QueryCommand AddUser = new(
        "INSERT INTO Users (Name, IsActive) VALUES (@name, 1) RETURNING ID");

    [Fact]
    public void A_cached_command_maps_a_buffered_list_of_objects() {
        using var cnn = db.GetConnection();

        var users = GetUsers.Query<List<QuickStartUser>>(cnn);

        Assert.Equal([
            new QuickStartUser(1, "John", null),
            new QuickStartUser(2, "Victor", "victor@corp.com"),
            new QuickStartUser(3, "Alice", "alice@corp.com")], users);
    }

    [Fact]
    public void The_result_type_selects_a_single_or_streamed_shape() {
        using var cnn = db.GetConnection();

        var one = GetUserById.Query<QuickStartUser>(cnn, new { id = 1 });
        Assert.Equal(new QuickStartUser(1, "John", null), one);

        var streamed = GetUsers.Query<IEnumerable<QuickStartUser>>(cnn).ToArray();
        Assert.Equal([1, 2, 3], streamed.Select(user => user.Id));
    }

    [Fact]
    public void An_anonymous_object_supplies_matching_parameter_names() {
        using var cnn = db.GetConnection();

        var user = GetUserById.Query<QuickStartUser>(cnn, new { ID = 2, ignored = "not a parameter" });

        Assert.Equal(new QuickStartUser(2, "Victor", "victor@corp.com"), user);
    }

    [Fact]
    public void Scalars_writes_and_optional_variables_follow_the_quick_start_examples() {
        using var cnn = db.GetConnection();
        Assert.Equal(3, CountUsers.Query<int>(cnn));
        Assert.Equal(3, CountUsers.ExecuteScalar<int>(cnn));
        Assert.Equal(4, AddUser.ExecuteScalar<int>(cnn, new { name = "New User" }));

        Assert.Equal(1, RenameUser.Execute(cnn, new { id = 2, name = "Victor" }));

        var search = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE Name LIKE ?@name AND ID > ?@afterId");
        var result = search.Query<List<QuickStartUser>>(cnn, new { name = "%John%" });
        Assert.Equal([new QuickStartUser(1, "John", null)], result);
    }
}

public sealed record QuickStartUser(int Id, string Name, string? Email);
