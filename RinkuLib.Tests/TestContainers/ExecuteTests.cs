using System;
using System.Collections.Generic;
using System.Data;
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

    private static readonly QueryCommand DropProc = new("IF OBJECT_ID('dbo.AddNumbers') IS NOT NULL DROP PROCEDURE dbo.AddNumbers");
    private static readonly QueryCommand CreateProc = new(
        "CREATE PROCEDURE dbo.AddNumbers @a INT, @b INT AS BEGIN SELECT @a + @b AS Total END");

    /// <summary>
    /// A procedure's name carries no variables to read, so the parameters are named at construction instead.
    /// Only a real provider can say whether the command was read as a procedure, since a mistaken
    /// <see cref="CommandType"/> makes the name arrive as SQL and fail there rather than here.
    /// </summary>
    [Fact]
    public async Task A_named_variable_command_runs_a_stored_procedure() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropProc.ExecuteAsync(cnn, ct: ct);
        await CreateProc.ExecuteAsync(cnn, ct: ct);

        var proc = new QueryCommand("dbo.AddNumbers", ["a", "b"]);
        Assert.Equal(7, await proc.QueryAsync<int>(cnn, new { a = 3, b = 4 }, ct: ct));
        Assert.Equal(11, await proc.QueryAsync<int>(cnn, new { a = 5, b = 6 }, ct: ct));
    }

    private static readonly QueryCommand DropDerived = new("IF OBJECT_ID('dbo.DescribeMe') IS NOT NULL DROP PROCEDURE dbo.DescribeMe");
    private static readonly QueryCommand CreateDerived = new(
        "CREATE PROCEDURE dbo.DescribeMe @name VARCHAR(50), @amount DECIMAL(9,2), @doubled DECIMAL(9,2) OUTPUT " +
        "AS BEGIN SET @doubled = @amount * 2; SELECT @name AS Name END");

    /// <summary>
    /// The procedure's own declaration is what the database holds, so asking it settles the names and the
    /// metadata together. What comes back is an ordinary command, and the parameters it binds carry the type,
    /// size and direction the procedure declared rather than what a first run happened to infer.
    /// </summary>
    [Fact]
    public async Task A_procedure_read_from_the_database_binds_what_it_declared() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropDerived.ExecuteAsync(cnn, ct: ct);
        await CreateDerived.ExecuteAsync(cnn, ct: ct);

        var proc = QueryCommand.FromProc("dbo.DescribeMe", cnn);

        Assert.Equal(CommandType.StoredProcedure, proc.CommandType);
        Assert.Equal(["@name", "@amount", "@doubled"], proc.Mapper.Keys.ToArray());

        var name = await proc.QueryAsync<string>(cnn, out var cmd,
            new { name = "Kashmir", amount = 1.25m, doubled = 0m }, ct: ct);
        using (cmd) {
            Assert.Equal("Kashmir", name);
            Assert.Equal(2.50m, (decimal)cmd.Parameters["@doubled"].Value!);
            Assert.Equal(ParameterDirection.InputOutput, cmd.Parameters["@doubled"].Direction);
            Assert.Equal(50, cmd.Parameters["@name"].Size);
        }
    }

    /// <summary>The same read through the async road, so a startup path can await it.</summary>
    [Fact]
    public async Task A_procedure_can_be_read_asynchronously() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropProc.ExecuteAsync(cnn, ct: ct);
        await CreateProc.ExecuteAsync(cnn, ct: ct);

        var proc = await StoredProcedure.FromAsync(cnn, "dbo.AddNumbers", ct: ct);

        Assert.Equal(["@a", "@b"], proc.Mapper.Keys.ToArray());
        Assert.Equal(9, await proc.QueryAsync<int>(cnn, new { a = 4, b = 5 }, ct: ct));
    }

    /// <summary>
    /// The provider's own reader is found beside its command by name, so a procedure is read with nothing
    /// registered. This is the claim the default rests on, and only a real provider can answer it.
    /// </summary>
    [Fact]
    public void The_provider_reader_is_found_without_being_registered() {
        Assert.Equal(nameof(StoredProcedure.DeriveThroughProvider), StoredProcedure.ParameterDeriver.Method.Name);
        using var cnn = Fixture.GetConnection();
        cnn.Open();
        using var cmd = cnn.CreateCommand();
        cmd.CommandText = "sp_help";
        cmd.CommandType = CommandType.StoredProcedure;
        StoredProcedure.DeriveThroughProvider(cmd);
        Assert.NotEmpty(cmd.Parameters);
    }

    /// <summary>
    /// Told to read a procedure, a provider takes the whole text as its name, so a name written with its
    /// variables beside it names no procedure at all. The variables belong on the command instead, which is
    /// what naming them does, and the text stays the name alone.
    /// </summary>
    [Fact]
    public async Task A_procedure_name_cannot_carry_its_variables_in_the_text() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropProc.ExecuteAsync(cnn, ct: ct);
        await CreateProc.ExecuteAsync(cnn, ct: ct);

        var written = new QueryCommand("dbo.AddNumbers @a, @b", ["a", "b"]);
        var ex = await Assert.ThrowsAsync<SqlException>(
            () => written.QueryAsync<int>(cnn, new { a = 1, b = 2 }, ct: ct));
        Assert.Contains("dbo.AddNumbers @a, @b", ex.Message);

        var named = new QueryCommand("dbo.AddNumbers", ["a", "b"]);
        Assert.Equal(3, await named.QueryAsync<int>(cnn, new { a = 1, b = 2 }, ct: ct));
    }

    /// <summary>
    /// A procedure reached through an <c>EXEC</c> is SQL, so its template is read the ordinary way and the
    /// provider is told to read the text as SQL rather than as a name.
    /// </summary>
    [Fact]
    public async Task A_procedure_reached_through_exec_stays_a_template() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropProc.ExecuteAsync(cnn, ct: ct);
        await CreateProc.ExecuteAsync(cnn, ct: ct);

        var viaExec = new QueryCommand("EXEC dbo.AddNumbers @a, @b");
        Assert.Equal(["@a", "@b"], viaExec.Mapper.Keys.ToArray());
        Assert.Equal(12, await viaExec.QueryAsync<int>(cnn, new { a = 5, b = 7 }, ct: ct));
    }

    /// <summary>The same procedure through a builder, which sets the values from code rather than an object.</summary>
    [Fact]
    public async Task A_named_variable_command_runs_from_a_builder() {
        using var cnn = Fixture.GetConnection();
        var ct = TestContext.Current.CancellationToken;
        await cnn.OpenAsync(ct);
        await DropProc.ExecuteAsync(cnn, ct: ct);
        await CreateProc.ExecuteAsync(cnn, ct: ct);

        var b = new QueryCommand("dbo.AddNumbers", ["@a", "@b"]).StartBuilder();
        b.Use("@a", 10);
        b.Use("@b", 32);
        Assert.Equal(42, await b.QueryAsync<int>(cnn, ct: ct));
    }
}