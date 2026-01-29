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
        Generator.Write = output.WriteLine;
#endif
        SQLitePCL.Batteries.Init();
    }
    public static DbConnection GetDbCnn() {
        return new SqliteConnection("Data Source=.\\TestDB.db;");
    }
    [Fact]
    public void Example1_StaticQuery() {
        var query = QueryCommand.New("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", false);
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = builder.QuerySingle<Person>(cnn);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Async() {
        var query = QueryCommand.New("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", false);
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = await builder.QuerySingleAsync<Person>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);
    }
}
public record Person(int ID, string Username, string Email);