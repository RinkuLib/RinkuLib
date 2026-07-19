using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// <c>Query&lt;T&gt;</c> runs the command and reads the first row as <c>T</c>, with collection and
/// wrapper shapes changing how many rows are read and how absence is handled.
/// </summary>
public class QueryTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    private static readonly QueryCommand ActiveUsers = new("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active ORDER BY ID");
    private static readonly QueryCommand AllUsers = new("SELECT ID, Name, Email FROM Users ORDER BY ID");
    private static readonly QueryCommand OneName = new("SELECT Name FROM Users WHERE ID = 1");
    private static readonly QueryCommand NoRows = new("SELECT Name FROM Users WHERE 1 = 0");
    private static readonly QueryCommand NoIntRows = new("SELECT ID FROM Users WHERE 1 = 0");
    private static readonly QueryCommand NullEmail = new("SELECT Email FROM Users WHERE ID = 1");
    private static readonly QueryCommand SalaryOfOne = new("SELECT Salary FROM Users WHERE ID = 1");

    [Fact]
    public void Scalar_string() {
        using var cnn = Db.GetConnection();
        Assert.Equal("John", OneName.Query<string>(cnn));
    }

    [Fact]
    public void Scalar_integer_converts_from_sqlite_long() {
        using var cnn = Db.GetConnection();
        var query = new QueryCommand("SELECT ID FROM Users WHERE ID = 2");
        Assert.Equal(2, query.Query<int>(cnn));
        Assert.Equal(2L, query.Query<long>(cnn));
        Assert.Equal((short)2, query.Query<short>(cnn));
    }

    [Fact]
    public void Scalar_decimal_converts_from_sqlite_double() {
        using var cnn = Db.GetConnection();
        Assert.Equal(10.5m, SalaryOfOne.Query<decimal>(cnn));
    }

    [Fact]
    public void Null_into_non_nullable_string_throws() {
        using var cnn = Db.GetConnection();
        Assert.Throws<NullValueAssignmentException>(() => NullEmail.Query<string>(cnn));
    }

    [Fact]
    public void Null_into_MaybeNull_returns_null() {
        using var cnn = Db.GetConnection();
        string? email = NullEmail.Query<MaybeNull<string>>(cnn);
        Assert.Null(email);
    }

    [Fact]
    public void No_rows_for_a_plain_type_throws() {
        using var cnn = Db.GetConnection();
        Refusals.Raises(ErrorCodes.NoRows, () => NoRows.Query<string>(cnn));
        Refusals.Raises(ErrorCodes.NoRows, () => NoIntRows.Query<int?>(cnn));
    }

    static readonly QueryCommand ByIds = new("SELECT Name FROM Users WHERE ID IN (@ids_X)");
    static readonly QueryCommand ByOptionalIds = new("SELECT Name FROM Users WHERE ID IN (?@ids_X)");

    /// <summary>
    /// The RINKU2002 path a caller actually takes. A spread builds the SQL out of its collection, so a
    /// query run without one is refused while the SQL is built, before any database call.
    /// </summary>
    [Fact]
    public void A_spread_run_without_its_collection_is_refused() {
        using var cnn = Db.GetConnection();
        Refusals.Raises(ErrorCodes.RequiredHandlerValue, () => ByIds.Query<List<string>>(cnn));

        Assert.Equal(2, ByIds.Query<List<string>>(cnn, new { ids = new[] { 1, 2 } }).Count);
        Assert.Equal(3, ByOptionalIds.Query<List<string>>(cnn).Count);
    }

    static readonly QueryCommand FirstName = new("SELECT Name FROM Users WHERE ID = @id");

    /// <summary>
    /// The RINKU3001 example. A scalar is converted at run time from whatever the query returned, so
    /// asking for a type the value does not convert to is refused there rather than at parser build.
    /// </summary>
    [Fact]
    public void A_scalar_asked_for_the_wrong_type_is_refused_at_conversion() {
        using var cnn = Db.GetConnection();
        Refusals.Raises(ErrorCodes.CannotConvert, () => FirstName.ExecuteScalar<int>(cnn, new { id = 1 }));
        Assert.Equal("John", FirstName.ExecuteScalar<string>(cnn, new { id = 1 }));
    }

    [Fact]
    public void No_rows_for_Optional_returns_null() {
        using var cnn = Db.GetConnection();
        string? name = NoRows.Query<Optional<string>>(cnn);
        Assert.Null(name);
    }

    [Fact]
    public void No_rows_for_OptionalStruct_returns_empty() {
        using var cnn = Db.GetConnection();
        int? id = NoIntRows.Query<OptionalStruct<int>>(cnn);
        Assert.False(id.HasValue);
    }

    [Fact]
    public void Single_with_exactly_one_row_returns_it() {
        using var cnn = Db.GetConnection();
        string name = OneName.Query<Single<string>>(cnn);
        Assert.Equal("John", name);
    }

    [Fact]
    public void Single_with_more_than_one_row_throws() {
        using var cnn = Db.GetConnection();
        Refusals.Raises(ErrorCodes.ShapeRefusedResult,
            () => AllUsers.Query<Single<(long, string, string?)>>(cnn));
    }

    [Fact]
    public void Record_maps_by_constructor_parameter_names() {
        using var cnn = Db.GetConnection();
        var user = ActiveUsers.Query<UserRow>(cnn, new { Active = true });
        Assert.NotNull(user);
        Assert.Equal(1, user.ID);
        Assert.Equal("John", user.Name);
        Assert.Null(user.Email);
    }

    [Fact]
    public void Alt_attribute_maps_a_renamed_member() {
        using var cnn = Db.GetConnection();
        var user = ActiveUsers.Query<NamedUser>(cnn, new { Active = false });
        Assert.NotNull(user);
        Assert.Equal(2, user.ID);
        Assert.Equal("Victor", user.Username);
        Assert.Equal("victor@corp.com", user.Email);
    }

    [Fact]
    public void Tuple_splits_columns_between_its_items() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Contact FROM Users ORDER BY ID");
        using var cnn = Db.GetConnection();
        var (user, contact) = query.Query<(ShortUser, object)>(cnn);
        Assert.Equal(1, user.ID);
        Assert.Equal("John", user.Username);
        Assert.Null(contact);
    }

    [Fact]
    public void List_reads_every_row() {
        using var cnn = Db.GetConnection();
        var users = AllUsers.Query<List<UserRow>>(cnn);
        Assert.NotNull(users);
        Assert.Equal(3, users.Count);
        Assert.Equal(["John", "Victor", "Alice"], users.Select(u => u.Name));
    }

    [Fact]
    public void List_with_no_rows_is_empty() {
        using var cnn = Db.GetConnection();
        var names = NoRows.Query<List<string>>(cnn);
        Assert.NotNull(names);
        Assert.Empty(names);
    }

    [Fact]
    public void DynaObject_exposes_columns_by_name() {
        using var cnn = Db.GetConnection();
        var row = AllUsers.Query<DynaObject>(cnn);
        Assert.NotNull(row);
        Assert.Equal(1L, row["ID"]);
        Assert.Equal("John", row["Name"]);
        Assert.Null(row["Email"]);
    }

    [Fact]
    public void Parameters_from_generic_object_overload() {
        using var cnn = Db.GetConnection();
        var user = ActiveUsers.Query<UserRow, ActiveFilter>(cnn, new ActiveFilter(false));
        Assert.NotNull(user);
        Assert.Equal("Victor", user.Name);
    }

    [Fact]
    public void Parameters_from_ref_struct_overload() {
        using var cnn = Db.GetConnection();
        var filter = new ActiveFilter(true);
        var user = ActiveUsers.Query<UserRow, ActiveFilter>(cnn, ref filter);
        Assert.NotNull(user);
        Assert.Equal("John", user.Name);
    }

    [Fact]
    public void Query_through_the_IDbConnection_path() {
        using var cnn = Db.GetConnection();
        var user = ActiveUsers.Query<UserRow>((System.Data.IDbConnection)cnn, new { Active = true });
        Assert.NotNull(user);
        Assert.Equal("John", user.Name);
    }

    [Fact]
    public void Query_leaves_a_closed_connection_closed() {
        using var cnn = Db.GetConnection();
        AllUsers.Query<UserRow>(cnn);
        Assert.Equal(System.Data.ConnectionState.Closed, cnn.State);
    }

    [Fact]
    public void Query_leaves_an_open_connection_open() {
        using var cnn = Db.Open();
        AllUsers.Query<UserRow>(cnn);
        Assert.Equal(System.Data.ConnectionState.Open, cnn.State);
    }

    [Fact]
    public void Query_with_out_command_hands_the_command_back() {
        using var cnn = Db.GetConnection();
        var user = AllUsers.Query<UserRow>(cnn, out var cmd);
        Assert.NotNull(user);
        Assert.NotNull(cmd);
        cmd.Dispose();
    }

    [Fact]
    public void Builder_query_renders_the_conditional_sql() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        using var cnn = Db.GetConnection();

        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        var active = builder.Query<UserRow>(cnn);
        Assert.NotNull(active);
        Assert.Equal("John", active.Name);

        var builder2 = query.StartBuilder([("@Active", 0)]);
        var inactive = builder2.Query<UserRow>(cnn);
        Assert.NotNull(inactive);
        Assert.Equal("Victor", inactive.Name);
    }

    [Fact]
    public void Command_bound_builder_reruns_with_updated_values() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        using var cnn = Db.GetConnection();
        using var cmd = cnn.CreateCommand();
        var builder = query.StartBuilder(cmd);

        builder.Use("@Active", true);
        var first = builder.Query<UserRow>();
        Assert.NotNull(first);
        Assert.Equal("John", first.Name);

        builder.Use("@Active", 0);
        var second = builder.Query<UserRow>();
        Assert.NotNull(second);
        Assert.Equal("Victor", second.Name);
    }

    [Fact]
    public void Default_valued_parameter_not_in_the_result_stays_default() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = Db.GetConnection();
        var row = query.Query<WithUnusedDefault>(cnn, new { ID = 1 });
        Assert.NotNull(row);
        Assert.Equal(1, row.ID);
        Assert.Equal("John", row.Name);
        Assert.Null(row.NotUsed);
    }
}

public record struct ActiveFilter(bool Active);
public record class WithUnusedDefault(long ID, string Name, int? NotUsed = null);
public record ShortUser(long ID, [Alt("Name")] string Username);
