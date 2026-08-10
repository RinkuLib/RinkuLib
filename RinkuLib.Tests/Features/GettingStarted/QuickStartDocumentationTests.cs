using Rinku;
using Rinku.Querying;
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
    public void A_parameter_object_can_be_a_class_record_or_struct() {
        using var cnn = db.GetConnection();

        var fromClass = GetUserById.Query<QuickStartUser>(cnn, new UserFilterClass { Id = 1 });
        var fromRecord = GetUserById.Query<QuickStartUser>(cnn, new UserFilterRecord(2));
        var fromStruct = GetUserById.Query<QuickStartUser>(cnn, new UserFilterStruct { Id = 3 });

        Assert.Equal(new QuickStartUser(1, "John", null), fromClass);
        Assert.Equal(new QuickStartUser(2, "Victor", "victor@corp.com"), fromRecord);
        Assert.Equal(new QuickStartUser(3, "Alice", "alice@corp.com"), fromStruct);
    }

    [Fact]
    public void Scalars_writes_and_optional_variables_follow_the_quick_start_examples() {
        using var cnn = db.GetConnection();
        try {
            Assert.Equal(3, CountUsers.Query<int>(cnn));
            Assert.Equal(3, CountUsers.ExecuteScalar<int>(cnn));
            Assert.Equal(4, AddUser.ExecuteScalar<int>(cnn, new { name = "New User" }));

            Assert.Equal(1, RenameUser.Execute(cnn, new { id = 2, name = "Victor" }));

            var search = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE Name LIKE ?@name AND ID > ?@afterId");
            var result = search.Query<List<QuickStartUser>>(cnn, new { name = "%John%" });
            Assert.Equal([new QuickStartUser(1, "John", null)], result);
        }
        finally {
            if (cnn.State != System.Data.ConnectionState.Open)
                cnn.Open();
            using var cleanup = cnn.CreateCommand();
            cleanup.CommandText = "DELETE FROM Users WHERE ID > 3; UPDATE Users SET Name = 'Victor' WHERE ID = 2";
            cleanup.ExecuteNonQuery();
        }
    }
}

public sealed record QuickStartUser(int Id, string Name, string? Email);

file sealed class UserFilterClass {
    public int Id { get; init; }
}

file sealed record UserFilterRecord(int Id);

file struct UserFilterStruct {
    public int Id { get; init; }
}
