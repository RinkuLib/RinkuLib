using System.Diagnostics.CodeAnalysis;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>
/// Classes and records map by name: constructor parameters first, settable members after, with
/// nullability enforced per slot.
/// </summary>
public class ObjectMappingTests {
    [Fact]
    public void Properties_map_by_name() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var user = Rows.ParseOne<PropUser>(cols, 1, "John Doe");
        Assert.Equal(1, user.Id);
        Assert.Equal("John Doe", user.Name);
    }

    [Fact]
    public void Column_name_matching_is_case_insensitive() {
        ColumnInfo[] cols = [new("ID", typeof(int), false), new("nAmE", typeof(string), false)];
        var user = Rows.ParseOne<PropUser>(cols, 3, "Jane");
        Assert.Equal(3, user.Id);
        Assert.Equal("Jane", user.Name);
    }

    [Fact]
    public void Parser_reads_consecutive_rows() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var users = Rows.ParseAll<PropUser>(cols, [1, "John"], [3, "Jane"]);
        Assert.Equal(2, users.Count);
        Assert.Equal(1, users[0].Id);
        Assert.Equal(3, users[1].Id);
    }

    [Fact]
    public void Record_constructor_parameters_map_by_name() {
        var badge = Guid.NewGuid();
        var joined = new DateTime(2023, 5, 10);
        ColumnInfo[] cols = [
            new("BadgeId", typeof(Guid), false),
            new("Department", typeof(string), false),
            new("Salary", typeof(decimal), false),
            new("JoinedAt", typeof(DateTime), false),
        ];
        var emp = Rows.ParseOne<EmployeeCard>(cols, badge, "Engineering", 95000.50m, joined);
        Assert.Equal(badge, emp.BadgeId);
        Assert.Equal("Engineering", emp.Department);
        Assert.Equal(95000.50m, emp.Salary);
        Assert.Equal(joined, emp.JoinedAt);
    }

    [Fact]
    public void Nullable_member_reads_value_and_null() {
        ColumnInfo[] cols = [
            new("ProductId", typeof(int), false),
            new("Weight", typeof(double), true),
            new("IsInStock", typeof(bool), false),
            new("WarehouseZone", typeof(char), false),
        ];
        var items = Rows.ParseAll<StockStatus>(cols,
            [500, 12.5, true, 'A'],
            [501, DBNull.Value, false, 'B']);
        Assert.Equal(12.5, items[0].Weight);
        Assert.Null(items[1].Weight);
        Assert.False(items[1].IsInStock);
        Assert.Equal('B', items[1].WarehouseZone);
    }

    [Fact]
    public void Binary_and_nullable_value_columns_map() {
        var salt = new byte[] { 0x01, 0x02, 0x03 };
        var expiration = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        ColumnInfo[] cols = [
            new("ID", typeof(int), false),
            new("Username", typeof(string), false),
            new("Email", typeof(string), false),
            new("Salt", typeof(byte[]), true),
            new("Token", typeof(string), true),
            new("ExpirationToken", typeof(DateTime), true),
            new("Valid", typeof(bool), false),
        ];
        var account = Rows.ParseOne<AccountSchema>(cols, 1, "JohnDoe", "john@example.com", salt, "secret-token", expiration, true);
        Assert.Equal(1, account.ID);
        Assert.Equal("JohnDoe", account.Username);
        Assert.Equal(salt, account.Salt);
        Assert.Equal("secret-token", account.Token);
        Assert.Equal(expiration, account.ExpirationToken);
        Assert.True(account.Valid);
    }

    [Fact]
    public void Schema_can_come_from_the_type_itself() {
        var parser = TypeParser.GetTypeParser<AccountSchema, AccountSchema>(out var cols);
        Assert.Equal(7, cols.Length);
        using var reader = Rows.Reader(cols, [2, "A", "a@b.c", DBNull.Value, DBNull.Value, DBNull.Value, false]);
        reader.Read();
        var account = parser.Parse(reader).Result;
        Assert.Equal(2, account.ID);
        Assert.Null(account.Salt);
    }

    [Fact]
    public void Parameter_defaulting_to_the_type_default_is_optional() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var user = Rows.ParseOne<UserWithDefault>(cols, 1, "x");
        Assert.Equal(1, user.Id);
        Assert.Equal("x", user.Name);
        Assert.Equal(0, user.Code);
    }

    [Fact]
    public void Parameter_with_a_non_default_default_stays_required() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        Assert.ThrowsAny<Exception>(() => {
            var localCols = cols;
            TypeParser.GetTypeParser<UserWithCustomDefault>(ref localCols);
        });
    }

    [Fact]
    public void Missing_required_column_fails_to_build_a_parser() {
        ColumnInfo[] cols = [new("Id", typeof(int), false)];
        Assert.ThrowsAny<Exception>(() => {
            var localCols = cols;
            TypeParser.GetTypeParser<PropUserRequired>(ref localCols);
        });
    }

    [Fact]
    public void NotNull_attribute_rejects_null_even_on_a_nullable_column() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Label", typeof(string), true)];
        var ok = Rows.ParseOne<GuardedLabel>(cols, 1, "fine");
        Assert.Equal("fine", ok.Label);
        Assert.Throws<NullValueAssignmentException>(() => Rows.ParseOne<GuardedLabel>(cols, 1, DBNull.Value));
    }

    [Fact]
    public void Public_fields_map_like_properties() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var row = Rows.ParseOne<FieldBag>(cols, 2, "field");
        Assert.Equal(2, row.Id);
        Assert.Equal("field", row.Name);
    }

    [Fact]
    public void Extra_columns_are_ignored() {
        ColumnInfo[] cols = [
            new("Junk1", typeof(string), true),
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("Junk2", typeof(double), true),
        ];
        var user = Rows.ParseOne<PropUser>(cols, "j", 4, "Kim", 1.0);
        Assert.Equal(4, user.Id);
        Assert.Equal("Kim", user.Name);
    }
}

public class PropUser {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
public record PropUserRequired(int Id, string Name);
public class EmployeeCard {
    public Guid BadgeId { get; set; }
    public string Department { get; set; } = "General";
    public decimal Salary { get; set; }
    public DateTime JoinedAt { get; set; }
}
public class StockStatus {
    public int ProductId { get; set; }
    public double? Weight { get; set; }
    public bool IsInStock { get; set; }
    public char WarehouseZone { get; set; }
}
public record AccountSchema(int ID, string Username, string Email, byte[]? Salt, string? Token, DateTime? ExpirationToken, bool Valid);
public record struct UserWithDefault(int Id, string Name, int Code = 0);
public record struct UserWithCustomDefault(int Id, string Name, int Code = -1);
public class GuardedLabel : IDbReadable {
    public int Id { get; set; }
    [NotNull] public string Label { get; set; } = null!;
}
public class FieldBag {
    public int Id;
    public string? Name;
}
