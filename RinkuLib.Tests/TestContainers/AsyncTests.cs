using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public class AsyncTestsFixture : DBFixture<SqlConnection> {
    public QueryCommand BasicStringUsage = new("select 'abc' as [Value] union all select @txt");
    public QueryCommand BasicStringUsageNULL = new("select NULL as [NullValue] union all select @txt");
    public QueryCommand BasicStringUsageSingle = new("select 'abc' as [Value]");
    public QueryCommand QueryWithDelay = new("waitfor delay '00:00:10';select 1");
    public QueryCommand DeclareVar = new("declare @foo table(id int not null); insert @foo values(@id);");
    public QueryCommand IdNameIdName = new("select 1 as id, 'abc' as name, 2 as id, 'def' as name");
    public QueryCommand IdNameCategoryIdName = new("select 1 as id, 'abc' as name, 2 as categoryId, 'def' as categoryName");
    public QueryCommand Select_3_4 = new("select 3 as [three], 4 as [four]");
    public QueryCommand DropTableLiteral = new("drop table literal1");
    public QueryCommand CreateTableLiteral = new("create table literal1 (id int not null, foo int not null)");
    public QueryCommand InsertInLiteral = new("insert literal1 (id,foo) values (@id_N, @foo)");
    public QueryCommand SelectCountLiteral = new("select count(1) from literal1 where id = @foo_N");
    public QueryCommand SelectSumLiteral = new("select sum(id) + sum(foo) from literal1");
    public QueryCommand CreateTableLiteralIn = new("create table #literalin(id int not null);");
    public QueryCommand InsertInLiteralin = new("insert #literalin (id) values (@id)");
    public QueryCommand SelectCountLiteralWithIn = new("select count(1) from #literalin where id in (@IDs_X)");
    public QueryCommand Select_1_2 = new("select 1; select 2");
    public QueryCommand SelectCol1Col2 = new("select Cast(1 as BigInt) Col1; select Cast(2 as BigInt) Col2");
    public QueryCommand Select_1_2_3_4_5 = new("select 1; select 2; select 3; select 4; select 5");
    public QueryCommand Select_Nothing_Int = new("select 1 where 1=0");
    public QueryCommand Select_Nothing_Str = new("select 'Test' where 1=0");
}
public class AsyncTests(AsyncTestsFixture Fixture) : IClassFixture<AsyncTestsFixture> {
    private readonly AsyncTestsFixture Fixture = Fixture;

    [Theory]
    [Repeat(2)]
    public async Task TestBasicStringUsageAsync(int _) {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.StreamQueryAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        var i = 0;
        string[] expecteds = ["abc", "def"];
        await foreach (var item in query)
            Assert.Equal(expecteds[i++], item);
    }
    [Theory]
    [Repeat(2)]
    public async Task TestBasicStringUsageDynaAsync(int _) {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.StreamQueryAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        var i = 0;
        string[] expecteds = ["abc", "def"];
        await foreach (var item in query)
            Assert.Equal(expecteds[i++], item["value"]);
    }
    [Fact]
    public async Task TestBasicStringUsageAsync_Cancellation() {
        using var cnn = Fixture.GetConnection();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var results = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            var query = Fixture.BasicStringUsage.StreamQueryAsync<string>(cnn, new { txt = "def" }, ct: cts.Token);

            await foreach (var value in query) {
                results.Add(value);
                cts.Cancel();
            }
        });

        Assert.Single(results);
        Assert.Equal("abc", results[0]);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync() {
        using var cnn = Fixture.GetConnection();
        var str = await Fixture.BasicStringUsage.QueryAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }
    
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsyncDynamic() {
        using var cnn = Fixture.GetConnection();
        var obj = await Fixture.BasicStringUsage.QueryAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(obj);
        Assert.Equal("abc", obj["value"]);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null() {
        using var cnn = Fixture.GetConnection();
        string? str = await Fixture.BasicStringUsageNULL.QueryAsync<MaybeNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Null(str);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null_Prevented() {
        using var cnn = Fixture.GetConnection();
        await Assert.ThrowsAnyAsync<NullValueAssignmentException>(async () => await Fixture.BasicStringUsageNULL.QueryAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Prevented() {
        using var cnn = Fixture.GetConnection();
        string str = await Fixture.BasicStringUsage.QueryAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsyncDynamic_Null() {
        using var cnn = Fixture.GetConnection();
        var obj = await Fixture.BasicStringUsageNULL.QueryAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(obj);
        Assert.Null(obj["nullvalue"]);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_NoParam() {
        using var cnn = Fixture.GetConnection();
        var str = await Fixture.BasicStringUsageSingle.QueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Single_Fail() {
        using var cnn = Fixture.GetConnection();
        await Assert.ThrowsAnyAsync<Exception>(async () => await Fixture.BasicStringUsage.QueryAsync<Single<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Single() {
        using var cnn = Fixture.GetConnection();
        var v = await Fixture.BasicStringUsageSingle.QueryAsync<Single<string>>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", v);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_OptionalStruct() {
        using var cnn = Fixture.GetConnection();
        int? v = await Fixture.Select_Nothing_Int.QueryAsync<OptionalStruct<int>>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.False(v.HasValue);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Optional_Fail() {
        using var cnn = Fixture.GetConnection();
        await Assert.ThrowsAnyAsync<Exception>(async () => await Fixture.Select_Nothing_Str.QueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(async () => await Fixture.Select_Nothing_Int.QueryAsync<int?>(cnn, ct: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Optional() {
        using var cnn = Fixture.GetConnection();
        string? v = await Fixture.Select_Nothing_Str.QueryAsync<Optional<string>>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.Null(v);
    }

    [Fact]
    public void TestLongOperationWithCancellation() {
        CancellationTokenSource cancel = new(TimeSpan.FromSeconds(5));
        using var cnn = Fixture.GetConnection();
        var task = Fixture.QueryWithDelay.QueryAsync<string>(cnn, ct: cancel.Token);
        try {
            if (!task.Wait(TimeSpan.FromSeconds(7))) {
                throw new TimeoutException();
            }
        }
        catch (AggregateException agg) {
            Assert.Equal("SqlException", agg.InnerException?.GetType().Name);
        }
    }
    
    [Fact]
    public async Task TestQueryDynamicAsync() {
        using var cnn = Fixture.GetConnection();
        var res = Fixture.BasicStringUsageSingle.StreamQueryAsync<string>(cnn, ct: TestContext.Current.CancellationToken);

        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("abc", enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task TestClassWithStringUsageAsync() {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.StreamQueryAsync<BasicType>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        var i = 0;
        string[] expecteds = ["abc", "def"];
        await foreach (var item in query)
            Assert.Equal(expecteds[i++], item.Value);
    }

    [Fact]
    public async Task TestExecuteAsync() {
        using var cnn = Fixture.GetConnection();
        var val = await Fixture.DeclareVar.ExecuteAsync(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, val);
    }

    [Fact]
    public async Task TestWithSplitAsync() {
        using var cnn = Fixture.GetConnection();
        var res = Fixture.IdNameIdName.StreamQueryAsync<(Product, Category)>(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);


        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var (product, category) = enumerator.Current;
        Assert.Equal(1, product.Id);
        Assert.Equal("abc", product.Name);
        Assert.Null(product.Category);
        Assert.Equal(2, category.Id);
        Assert.Equal("def", category.Name);
        Assert.Null(category.Description);
        Assert.False(await enumerator.MoveNextAsync());
    }
    [Fact]
    public async Task TestMultiMapWithSplitAsync() {
        using var cnn = Fixture.GetConnection();
        var res = Fixture.IdNameCategoryIdName.StreamQueryAsync<Product>(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);

        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var product = enumerator.Current;
        Assert.Equal(1, product.Id);
        Assert.Equal("abc", product.Name);
        Assert.NotNull(product.Category);
        Assert.Equal(2, product.Category.Id);
        Assert.Equal("def", product.Category.Name);
        Assert.Null(product.Category.Description);
        Assert.False(await enumerator.MoveNextAsync());
    }
    [Fact]
    public async Task TestMultiAsync() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        using var multi = await Fixture.Select_1_2.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        var res1 = multi.StreamQueryAsync<int>(ct : TestContext.Current.CancellationToken);
        await using var enumerator = res1.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item1 = enumerator.Current;
        Assert.Equal(1, item1);
        Assert.False(await enumerator.MoveNextAsync());
        var item2 = await multi.QueryAsync<int>(ct : TestContext.Current.CancellationToken);
        Assert.Equal(2, item2);
        cmd.Dispose();
    }

    [Fact]
    public async Task TestMultiConversionAsync() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        using var multi = await Fixture.SelectCol1Col2.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryAsync<int>(ct :TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
        cmd.Dispose();
    }

    [Fact]
    public async Task TestMultiAsyncViaFirstOrDefault() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        using var multi = await Fixture.Select_1_2_3_4_5.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryAsync<int>(ct :TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.StreamQueryAsync<int>(ct :TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
        var item3 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, item3);
        var res4 = multi.StreamQueryAsync<int>(ct:TestContext.Current.CancellationToken);
        await using var enumerator4 = res4.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator4.MoveNextAsync());
        var item4 = enumerator4.Current;
        Assert.Equal(4, item4);
        Assert.False(await enumerator4.MoveNextAsync());
        var item5 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(5, item5);
        cmd.Dispose();
    }

    [Fact]
    public async Task TestMultiClosedConnAsync() {
        using var cnn = Fixture.GetConnection();
        using var multi = await Fixture.Select_1_2.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        var res1 = multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken);
        await using var enumerator = res1.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item1 = enumerator.Current;
        Assert.Equal(1, item1);
        Assert.False(await enumerator.MoveNextAsync());
        var item2 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, item2);
        cmd.Dispose();
    }

    [Fact]
    public async Task TestMultiClosedConnAsyncViaFirstOrDefault() {
        using var cnn = Fixture.GetConnection();
        using var multi = await Fixture.Select_1_2_3_4_5.ExecuteMultiReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
        var item3 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, item3);
        var res4 = multi.StreamQueryAsync<int>(ct: TestContext.Current.CancellationToken);
        await using var enumerator4 = res4.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator4.MoveNextAsync());
        var item4 = enumerator4.Current;
        Assert.Equal(4, item4);
        Assert.False(await enumerator4.MoveNextAsync());
        var item5 = await multi.QueryAsync<int>(ct: TestContext.Current.CancellationToken);
        Assert.Equal(5, item5);
        cmd.Dispose();
    }
    [Fact]
    public async Task ExecuteReaderOpenAsync() {
        var dt = new DataTable();
        using var cnn = Fixture.GetConnection();
        cnn.Open();
        dt.Load(await Fixture.Select_3_4.ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken));
        Assert.Equal(2, dt.Columns.Count);
        Assert.Equal("three", dt.Columns[0].ColumnName);
        Assert.Equal("four", dt.Columns[1].ColumnName);
        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal(3, (int)dt.Rows[0][0]);
        Assert.Equal(4, (int)dt.Rows[0][1]);
        cmd.Dispose();
    }

    [Fact]
    public async Task ExecuteReaderClosedAsync() {
        var dt = new DataTable();
        using var cnn = Fixture.GetConnection();
        cnn.Close();
        dt.Load(await Fixture.Select_3_4.ExecuteReaderAsync(cnn, out var cmd, ct: TestContext.Current.CancellationToken));
        Assert.Equal(2, dt.Columns.Count);
        Assert.Equal("three", dt.Columns[0].ColumnName);
        Assert.Equal("four", dt.Columns[1].ColumnName);
        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal(3, (int)dt.Rows[0][0]);
        Assert.Equal(4, (int)dt.Rows[0][1]);
        cmd.Dispose();
    }
    
    [Fact]
    public async Task LiteralReplacementOpen() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        await LiteralReplacement(cnn);
    }

    [Fact]
    public async Task LiteralReplacementClosed() {
        using var cnn = Fixture.GetConnection();
        await LiteralReplacement(cnn);
    }

    private async Task LiteralReplacement(DbConnection conn) {
        try {
            await Fixture.DropTableLiteral.ExecuteAsync(conn, ct: TestContext.Current.CancellationToken);
        }
        catch {
            //don't care
        }
        await Fixture.CreateTableLiteral.ExecuteAsync(conn, ct: TestContext.Current.CancellationToken);
        var builder = Fixture.InsertInLiteral.StartBuilder(conn.CreateCommand());
        builder.UseWith(new { id = 123, foo = 456 });
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        builder.UseWith(new { id = 1, foo = 2 });
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        builder.UseWith(new { id = 3, foo = 4 });
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        var count = (await Fixture.SelectCountLiteral.QueryAsync<List<int>>(conn, new { foo = 123 }, ct: TestContext.Current.CancellationToken))!.Single();
        Assert.Equal(1, count);
        var sum = (await Fixture.SelectSumLiteral.QueryAsync<List<int>>(conn, ct: TestContext.Current.CancellationToken))!.Single();
        Assert.Equal(123 + 456 + 1 + 2 + 3 + 4, sum);
    }
    private static readonly int[] IDs = [1, 3, 4];

    [Fact]
    public async Task LiteralInAsync() {

        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        await Fixture.CreateTableLiteralIn.ExecuteAsync(cnn, ct: TestContext.Current.CancellationToken);
        using var boundCmd = cnn.CreateCommand();
        var builder = Fixture.InsertInLiteralin.StartBuilder(boundCmd);
        builder.Use("@id", 1);
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        builder.Use("@id", 2);
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        builder.Use("@id", 3);
        await builder.ExecuteAsync(ct: TestContext.Current.CancellationToken);
        var count = (await Fixture.SelectCountLiteralWithIn.QueryAsync<List<int>>(cnn,
            new { ids = IDs }, ct: TestContext.Current.CancellationToken))!.Single();
        Assert.Equal(2, count);
    }
}

public class BasicType {
    public string? Value { get; set; }
}
public class Product {
    public int Id { get; set; }
    public string? Name { get; set; }
    public Category? Category { get; set; }
}
public class Category : IDbReadable {
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}