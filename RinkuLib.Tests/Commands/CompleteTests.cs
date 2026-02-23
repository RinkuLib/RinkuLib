using System.Data.Common;
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
}
public record struct  PersonParam(bool Active);
public record Person(int ID, [Alt("Name")]string Username, string? Email) : IDbReadable {
    public Person(int ID, [Alt("Name")]string Username) :this(ID, Username, null) { }
}