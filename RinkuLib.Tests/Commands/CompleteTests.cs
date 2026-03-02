using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.Commands; 
public class CompleteTests {
    private readonly ITestOutputHelper _output;
    public CompleteTests(ITestOutputHelper output) {
        _output = output;
#if DEBUG
        //Generator.Write = output.WriteLine;
#endif
        SQLitePCL.Batteries.Init();
    }
    public static DbConnection GetDbCnn() {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDB.db");
        return new SqliteConnection($"Data Source={dbPath}");
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null_Prevented() {
        QueryCommand BasicStringUsageNULL = new("select 'value' as [Value] union all select CAST(NULL AS TEXT) union all select @txt");
        using var cnn = GetDbCnn();

        var res = BasicStringUsageNULL.QueryAllAsync<NotNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);

        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("value", (string)enumerator.Current);

        await Assert.ThrowsAsync<NullValueAssignmentException>(async () => {
            await enumerator.MoveNextAsync();
        });
    }

    [Fact]
    public void Example1_StaticQuery() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = builder.QueryOne<Person>(cnn);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);

        var builder2 = query.StartBuilder([("@Active", 0)]);
        var p2 = builder2.QueryOne<Person>(cnn);
        Assert.NotNull(p2);
        Assert.Equal(2, p2.ID);
        Assert.Equal("Victor", p2.Username);
        Assert.Equal("abc@email.com", p2.Email);
    }
    [Fact]
    public void Double_to_Decimal() {
        var query = new QueryCommand("SELECT Number FROM Users WHERE ID = 1");
        using var cnn = GetDbCnn();
        var d = query.QueryOne<decimal>(cnn);
        Assert.Equal(10.2M, d);
    }
    [Fact]
    public void Example1_StaticQuery_Reuse() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        using var cmd = cnn.CreateCommand();
        var builder = query.StartBuilder(cmd);
        builder.Use("@Active", true);
        var p = builder.QueryOne<Person>();
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);

        builder.Use("@Active", 0);
        var p2 = builder.QueryOne<Person>();
        Assert.NotNull(p2);
        Assert.Equal(2, p2.ID);
        Assert.Equal("Victor", p2.Username);
        Assert.Equal("abc@email.com", p2.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Async() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = await builder.QueryOneAsync<Person>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Object() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var (p, email) = builder.QueryOne<(Person, object)>(cnn);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_Obj() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object)>(cnn, new PersonParam(true));
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_T() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object), PersonParam>(cnn, new PersonParam(true));
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_T_False() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object), PersonParam>(cnn, new PersonParam(false));
        Assert.Equal(2, p.ID);
        Assert.Equal("Victor", p.Username);
        Assert.Equal("abc@email.com", email);
    }
    [Fact]
    public void Using_Two_DifferentAnonymous() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        using var cnn = GetDbCnn();
        nint handle1 = default;
        using (var cmd1 = cnn.CreateCommand()) {
            var val1 = new { ID = 1 };
            handle1 = val1.GetType().TypeHandle.Value;
            Span<bool> usageMap1 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd1, val1, usageMap1);
            Assert.Equal("SELECT * FROM u WHERE u.ID = @ID", cmd1.CommandText);
            Assert.True(usageMap1[0]);
            Assert.False(usageMap1[1]);
        }
        using (var cmd2 = cnn.CreateCommand()) {
            var val2 = new { OtherID = 1 };
            Assert.NotEqual(handle1, val2.GetType().TypeHandle.Value);
            Span<bool> usageMap2 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd2, val2, usageMap2);
            Assert.Equal("SELECT * FROM u WHERE u.OtherID = @OtherID", cmd2.CommandText);
            Assert.False(usageMap2[0]);
            Assert.True(usageMap2[1]);
        }
    }
    [Fact]
    public void Using_Two_DifferentTyped() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        using var cnn = GetDbCnn();
        using (var cmd1 = cnn.CreateCommand()) {
            Span<bool> usageMap1 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd1, new A1 { ID = 1 }, usageMap1);
            Assert.Equal("SELECT * FROM u WHERE u.ID = @ID", cmd1.CommandText);
            Assert.True(usageMap1[0]);
            Assert.False(usageMap1[1]);
        }
        using (var cmd2 = cnn.CreateCommand()) {
            Span<bool> usageMap2 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd2, new A2 { OtherID = 1 }, usageMap2);
            Assert.Equal("SELECT * FROM u WHERE u.OtherID = @OtherID", cmd2.CommandText);
            Assert.False(usageMap2[0]);
            Assert.True(usageMap2[1]);
        }
    }
}
public record struct  PersonParam(bool Active);
public record Person(int ID, [Alt("Name")]string Username, string? Email) : IDbReadable {
    public Person(int ID, [Alt("Name")]string Username) :this(ID, Username, null) { }
}
public sealed class A1 {
    public int ID;
}
public sealed class A2 {
    public int OtherID;
}