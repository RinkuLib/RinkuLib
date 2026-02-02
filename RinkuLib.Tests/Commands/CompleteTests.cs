using System.Data.Common;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
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
        return new SqliteConnection("Data Source=.\\TestDB.db;");
    }
    [Fact]
    public void Example1_StaticQuery() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = builder.QuerySingle<Person>(cnn);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);

        var builder2 = query.StartBuilderWith(("@Active", 0));
        var p2 = builder2.QuerySingle<Person>(cnn);
        Assert.NotNull(p2);
        Assert.Equal(2, p2.ID);
        Assert.Equal("Victor", p2.Username);
        Assert.Equal("abc@email.com", p2.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Async() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = await builder.QuerySingleAsync<Person>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Object() {
        var query = new QueryCommand("SELECT ID, Username, Email AS Emaill FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var (p, email) = builder.QuerySingle<(Person, object)>(cnn);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
}
public record Person(int ID, string Username, string? Email) : IDbReadable {
    public Person(int ID, string Username) :this(ID, Username, null) { }
}