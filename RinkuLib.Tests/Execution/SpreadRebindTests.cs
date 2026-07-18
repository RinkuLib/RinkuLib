using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Execution;

/// <summary>
/// A spread on a bound command re-binds differentially between runs: same count updates in place, fewer
/// removes the tail, more appends, and an absent value strips every parameter. Each transition re-executes
/// against the database so the bound SQL and parameters are verified together.
/// </summary>
public class SpreadRebindTests(SqliteDb Db) : IClassFixture<SqliteDb> {
    [Fact]
    public void Bound_spread_rebinds_through_every_size_transition() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@Ids_X)");
        using var cnn = Db.Open();
        var b = query.StartBuilder(cnn.CreateCommand());

        b.Use("@Ids", new[] { 1, 2, 3 });                      
        Assert.Equal(3, b.ExecuteScalar<int>());

        b.Use("@Ids", new[] { 3, 2, 9 });                     
        Assert.Equal(2, b.ExecuteScalar<int>());

        b.Use("@Ids", new[] { 2 });                           
        Assert.Equal(1, b.ExecuteScalar<int>());

        b.Use("@Ids", Enumerable.Range(1, 11).ToArray());     
        Assert.Equal(3, b.ExecuteScalar<int>());

        b.Use("@Ids", new[] { 1 });                    
        Assert.Equal(1, b.ExecuteScalar<int>());

        b.Use("@Ids", null);                                
        Assert.Equal(3, b.ExecuteScalar<int>());

        b.Use("@Ids", new[] { 2, 3 });                   
        Assert.Equal(2, b.ExecuteScalar<int>());
    }

    [Fact]
    public void Bound_spread_dropped_by_an_empty_collection_and_rebound() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@Ids_X)");
        using var cnn = Db.Open();
        var b = query.StartBuilder(cnn.CreateCommand());

        b.Use("@Ids", new[] { 1, 2 });
        Assert.Equal(2, b.ExecuteScalar<int>());

        b.Use("@Ids", Array.Empty<int>());                
        Assert.Equal(3, b.ExecuteScalar<int>());

        b.Use("@Ids", new[] { 3 });
        Assert.Equal(1, b.ExecuteScalar<int>());
    }

    [Fact]
    public void Learned_parameter_metadata_stops_the_relearning() {
        var query = new QueryCommand("SELECT COUNT(*) FROM Users WHERE ID IN (?@Ids_X) AND IsActive = ?@Active");
        using var cnn = Db.Open();
        using var cmd = cnn.CreateCommand();

        var variables = new object?[query.Mapper.Count];
        variables[query.Mapper.GetIndex("@Active")] = 1;
        variables[query.Mapper.GetIndex("@Ids")] = new[] { 1, 2, 3 };
        query.SetCommand(cmd, variables);
        Assert.Equal(2L, cmd.ExecuteScalar());

        Assert.True(query.Parameters.NeedToCache(variables));
        query.UpdateCache(cmd);

        var activeOnly = new object?[query.Mapper.Count];
        activeOnly[query.Mapper.GetIndex("@Active")] = 1;
        Assert.False(query.Parameters.NeedToCache(activeOnly));

        Assert.True(query.Parameters.NeedToCache(variables));

        query.UpdateCache(cmd);
        Assert.False(query.Parameters.NeedToCache(activeOnly));
    }
}
