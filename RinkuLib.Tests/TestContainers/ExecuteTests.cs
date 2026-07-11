using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.TestContainers; 
public class ExecuteTestsFixture : DBFixture<SqlConnection> {
    public QueryCommand CreateSimpleTable = new("CREATE TABLE #Simple (ID INT IDENTITY(1,1), Val INT)");
    public QueryCommand InsertAndGetId = new("INSERT INTO #Simple (Val) VALUES (@Val); SELECT SCOPE_IDENTITY();");
    public QueryCommand SelectNull = new("SELECT ID FROM #Simple WHERE ID = 100");
}
public class ExecuteTests(ExecuteTestsFixture fixture) : IClassFixture<ExecuteTestsFixture> {
    private readonly ExecuteTestsFixture Fixture = fixture;

    [Fact]
    public async Task TestIdentityConversion_ToLong() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await Fixture.CreateSimpleTable.ExecuteAsync(cnn, ct: ct);
        var id = await Fixture.InsertAndGetId.ExecuteScalarAsync<long>(cnn, new { Val = 10 }, ct: ct);
        Assert.Equal(1L, id);
    }

    [Fact]
    public async Task TestIdentityConversion_ToUInt() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await Fixture.CreateSimpleTable.ExecuteAsync(cnn, ct: ct);
        var id = await Fixture.InsertAndGetId.ExecuteScalarAsync<uint>(cnn, new { Val = 10 }, ct: ct);
        Assert.Equal(1U, id);
    }

    [Fact]
    public async Task TestNullableIdentity_Long() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await Fixture.CreateSimpleTable.ExecuteAsync(cnn, ct: ct);
        var id = await Fixture.SelectNull.ExecuteScalarAsync<long?>(cnn, ct: ct);
        Assert.Null(id);
    }

    [Fact]
    public async Task TestNullableIdentity_Int() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await Fixture.CreateSimpleTable.ExecuteAsync(cnn, ct: ct);
        var id = await Fixture.SelectNull.ExecuteScalarAsync<int?>(cnn, ct: ct);
        Assert.Null(id);
    }
}