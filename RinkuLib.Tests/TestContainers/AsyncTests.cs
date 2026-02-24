using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.Commands;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.TestContainers;

public class AsyncTestsFixture : DBFixture<SqlConnection> {
    public QueryCommand BasicStringUsage = new("select 'abc' as [Value] union all select @txt");
    public QueryCommand BasicStringUsageNULL = new("select NULL as [Value] union all select @txt");
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
}
public class AsyncTests(AsyncTestsFixture Fixture) : IClassFixture<AsyncTestsFixture> {
    private readonly AsyncTestsFixture Fixture = Fixture;

    [Theory]
    [Repeat(2)]
    public async Task TestBasicStringUsageAsync(int _) {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.QueryAllAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        var i = 0;
        string[] expecteds = ["abc", "def"];
        await foreach (var item in query)
            Assert.Equal(expecteds[i++], item);
    }
    [Theory]
    [Repeat(2)]
    public async Task TestBasicStringUsageDynaAsync(int _) {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.QueryAllAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
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
            var query = Fixture.BasicStringUsage.QueryAllAsync<string>(cnn, new { txt = "def" }, ct: cts.Token);

            await foreach (var value in query) {
                results.Add(value);
                cts.Cancel(); // Manually trigger the cancellation logic
            }
        });

        Assert.Single(results);
        Assert.Equal("abc", results[0]);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync() {
        using var cnn = Fixture.GetConnection();
        var str = await Fixture.BasicStringUsage.QueryOneAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }
    
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsyncDynamic() {
        using var cnn = Fixture.GetConnection();
        var obj = await Fixture.BasicStringUsage.QueryOneAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(obj);
        Assert.Equal("abc", obj["value"]);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null() {
        using var cnn = Fixture.GetConnection();
        var str = await Fixture.BasicStringUsageNULL.QueryOneAsync<string>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Null(str);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null_Prevented() {
        using var cnn = Fixture.GetConnection();
        await Assert.ThrowsAnyAsync<NullValueAssignmentException>(async () => await Fixture.BasicStringUsageNULL.QueryOneAsync<NotNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Prevented() {
        using var cnn = Fixture.GetConnection();
        string str = await Fixture.BasicStringUsage.QueryOneAsync<NotNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }

    [Fact]
    public async Task TestBasicStringUsageQueryOneAsyncDynamic_Null() {
        using var cnn = Fixture.GetConnection();
        var obj = await Fixture.BasicStringUsageNULL.QueryOneAsync<DynaObject>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(obj);
        Assert.Null(obj["value"]);
    }
    
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_NoParam() {
        using var cnn = Fixture.GetConnection();
        var str = await Fixture.BasicStringUsageSingle.QueryOneAsync<string>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.Equal("abc", str);
    }

    [Fact]
    public void TestLongOperationWithCancellation() {
        CancellationTokenSource cancel = new(TimeSpan.FromSeconds(5));
        using var cnn = Fixture.GetConnection();
        var task = Fixture.QueryWithDelay.QueryOneAsync<string>(cnn, ct: cancel.Token);
        try {
            if (!task.Wait(TimeSpan.FromSeconds(7))) {
                throw new TimeoutException(); // should have cancelled
            }
        }
        catch (AggregateException agg) {
            Assert.Equal("SqlException", agg.InnerException?.GetType().Name);
        }
    }
    
    [Fact]
    public async Task TestQueryDynamicAsync() {
        using var cnn = Fixture.GetConnection();
        var res = Fixture.BasicStringUsageSingle.QueryAllAsync<string>(cnn, ct: TestContext.Current.CancellationToken);

        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("abc", enumerator.Current);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task TestClassWithStringUsageAsync() {
        using var cnn = Fixture.GetConnection();
        var query = Fixture.BasicStringUsage.QueryAllAsync<BasicType>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);
        var i = 0;
        string[] expecteds = ["abc", "def"];
        await foreach (var item in query)
            Assert.Equal(expecteds[i++], item.Value);
    }

    [Fact]
    public async Task TestExecuteAsync() {
        using var cnn = Fixture.GetConnection();
        var val = await Fixture.DeclareVar.ExecuteQueryAsync(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, val);
    }

    [Fact]
    public async Task TestWithSplitAsync() {
        using var cnn = Fixture.GetConnection();
        var res = Fixture.IdNameIdName.QueryAllAsync<(Product, Category)>(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);


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
        var res = Fixture.IdNameCategoryIdName.QueryAllAsync<Product>(cnn, new { id = 1 }, ct: TestContext.Current.CancellationToken);

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
        using var multi = await Fixture.Select_1_2.ExecuteMultiReaderAsync(cnn, ct: TestContext.Current.CancellationToken);
        var res1 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator = res1.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item1 = enumerator.Current;
        Assert.Equal(1, item1);
        Assert.False(await enumerator.MoveNextAsync());
        var item2 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(2, item2);
    }

    [Fact]
    public async Task TestMultiConversionAsync() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        using var multi = await Fixture.SelectCol1Col2.ExecuteMultiReaderAsync(cnn, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task TestMultiAsyncViaFirstOrDefault() {
        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        using var multi = await Fixture.Select_1_2_3_4_5.ExecuteMultiReaderAsync(cnn, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
        var item3 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(3, item3);
        var res4 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator4 = res4.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator4.MoveNextAsync());
        var item4 = enumerator4.Current;
        Assert.Equal(4, item4);
        Assert.False(await enumerator4.MoveNextAsync());
        var item5 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(5, item5);
    }

    [Fact]
    public async Task TestMultiClosedConnAsync() {
        using var cnn = Fixture.GetConnection();
        using var multi = await Fixture.Select_1_2.ExecuteMultiReaderAsync(cnn, ct: TestContext.Current.CancellationToken);
        var res1 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator = res1.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item1 = enumerator.Current;
        Assert.Equal(1, item1);
        Assert.False(await enumerator.MoveNextAsync());
        var item2 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(2, item2);
    }

    [Fact]
    public async Task TestMultiClosedConnAsyncViaFirstOrDefault() {
        using var cnn = Fixture.GetConnection();
        using var multi = await Fixture.Select_1_2_3_4_5.ExecuteMultiReaderAsync(cnn, ct: TestContext.Current.CancellationToken);
        var item1 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(1, item1);
        var res2 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator = res2.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        var item2 = enumerator.Current;
        Assert.Equal(2, item2);
        Assert.False(await enumerator.MoveNextAsync());
        var item3 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(3, item3);
        var res4 = multi.QueryAllAsync<int>(TestContext.Current.CancellationToken);
        await using var enumerator4 = res4.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator4.MoveNextAsync());
        var item4 = enumerator4.Current;
        Assert.Equal(4, item4);
        Assert.False(await enumerator4.MoveNextAsync());
        var item5 = await multi.QueryOneAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(5, item5);
    }
    [Fact]
    public async Task ExecuteReaderOpenAsync() {
        var dt = new DataTable();
        using var cnn = Fixture.GetConnection();
        cnn.Open();
        dt.Load(await Fixture.Select_3_4.ExecuteReaderAsync(cnn, ct: TestContext.Current.CancellationToken));
        Assert.Equal(2, dt.Columns.Count);
        Assert.Equal("three", dt.Columns[0].ColumnName);
        Assert.Equal("four", dt.Columns[1].ColumnName);
        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal(3, (int)dt.Rows[0][0]);
        Assert.Equal(4, (int)dt.Rows[0][1]);
    }

    [Fact]
    public async Task ExecuteReaderClosedAsync() {
        var dt = new DataTable();
        using var cnn = Fixture.GetConnection();
        cnn.Close();
        dt.Load(await Fixture.Select_3_4.ExecuteReaderAsync(cnn, ct: TestContext.Current.CancellationToken));
        Assert.Equal(2, dt.Columns.Count);
        Assert.Equal("three", dt.Columns[0].ColumnName);
        Assert.Equal("four", dt.Columns[1].ColumnName);
        Assert.Equal(1, dt.Rows.Count);
        Assert.Equal(3, (int)dt.Rows[0][0]);
        Assert.Equal(4, (int)dt.Rows[0][1]);
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
            await Fixture.DropTableLiteral.ExecuteQueryAsync(conn, ct: TestContext.Current.CancellationToken);
        }
        catch {
            //don't care
        }
        await Fixture.CreateTableLiteral.ExecuteQueryAsync(conn, ct: TestContext.Current.CancellationToken);
        var builder = Fixture.InsertInLiteral.StartBuilder(conn.CreateCommand());
        builder.UseWith(new { id = 123, foo = 456 });
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        builder.UseWith(new { id = 1, foo = 2 });
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        builder.UseWith(new { id = 3, foo = 4 });
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        var count = (await Fixture.SelectCountLiteral.QueryAllBufferedAsync<int>(conn, new { foo = 123 }, ct: TestContext.Current.CancellationToken)).Single();
        Assert.Equal(1, count);
        var sum = (await Fixture.SelectSumLiteral.QueryAllBufferedAsync<int>(conn, ct: TestContext.Current.CancellationToken)).Single();
        Assert.Equal(123 + 456 + 1 + 2 + 3 + 4, sum);
    }

    [Fact]
    public async Task LiteralInAsync() {

        using var cnn = Fixture.GetConnection();
        await cnn.OpenAsync(TestContext.Current.CancellationToken);
        await Fixture.CreateTableLiteralIn.ExecuteQueryAsync(cnn, ct: TestContext.Current.CancellationToken);
        var builder = Fixture.InsertInLiteralin.StartBuilder(cnn.CreateCommand());
        builder.Use("@id", 1);
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        builder.Use("@id", 2);
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        builder.Use("@id", 3);
        await builder.ExecuteQueryAsync(ct: TestContext.Current.CancellationToken);
        var count = (await Fixture.SelectCountLiteralWithIn.QueryAllBufferedAsync<int>(cnn,
            new { ids = new[] { 1, 3, 4 } }, ct: TestContext.Current.CancellationToken)).Single();
        Assert.Equal(2, count);
    }
    /*
    [FactLongRunning]
    public async Task RunSequentialVersusParallelAsync() {
        var ids = Enumerable.Range(1, 20000).Select(id => new { id }).ToArray();
        await MarsConnection.ExecuteAsync(new CommandDefinition("select @id", ids.Take(5), flags: CommandFlags.None)).ConfigureAwait(false);

        var watch = Stopwatch.StartNew();
        await MarsConnection.ExecuteAsync(new CommandDefinition("select @id", ids, flags: CommandFlags.None)).ConfigureAwait(false);
        watch.Stop();
        Console.WriteLine("No pipeline: {0}ms", watch.ElapsedMilliseconds);

        watch = Stopwatch.StartNew();
        await MarsConnection.ExecuteAsync(new CommandDefinition("select @id", ids, flags: CommandFlags.Pipelined)).ConfigureAwait(false);
        watch.Stop();
        Console.WriteLine("Pipeline: {0}ms", watch.ElapsedMilliseconds);
    }

    [FactLongRunning]
    public void RunSequentialVersusParallelSync() {
        var ids = Enumerable.Range(1, 20000).Select(id => new { id }).ToArray();
        MarsConnection.Execute(new CommandDefinition("select @id", ids.Take(5), flags: CommandFlags.None));

        var watch = Stopwatch.StartNew();
        MarsConnection.Execute(new CommandDefinition("select @id", ids, flags: CommandFlags.None));
        watch.Stop();
        Console.WriteLine("No pipeline: {0}ms", watch.ElapsedMilliseconds);

        watch = Stopwatch.StartNew();
        MarsConnection.Execute(new CommandDefinition("select @id", ids, flags: CommandFlags.Pipelined));
        watch.Stop();
        Console.WriteLine("Pipeline: {0}ms", watch.ElapsedMilliseconds);
    }


    [Fact]
    public async Task TypeBasedViaTypeAsync() {
        Type type = Common.GetSomeType();

        dynamic actual = (await MarsConnection.QueryAsync(type, "select @A as [A], @B as [B]", new { A = 123, B = "abc" }).ConfigureAwait(false)).FirstOrDefault()!;
        Assert.Equal(((object)actual).GetType(), type);
        int a = actual.A;
        string b = actual.B;
        Assert.Equal(123, a);
        Assert.Equal("abc", b);
    }

    [Fact]
    public async Task TypeBasedViaTypeAsyncFirstOrDefault() {
        Type type = Common.GetSomeType();

        dynamic actual = (await MarsConnection.QueryFirstOrDefaultAsync(type, "select @A as [A], @B as [B]", new { A = 123, B = "abc" }).ConfigureAwait(false))!;
        Assert.Equal(((object)actual).GetType(), type);
        int a = actual.A;
        string b = actual.B;
        Assert.Equal(123, a);
        Assert.Equal("abc", b);
    }

    [Fact]
    public async Task Issue22_ExecuteScalarAsync() {
        int i = await connection.ExecuteScalarAsync<int>("select 123").ConfigureAwait(false);
        Assert.Equal(123, i);

        i = await connection.ExecuteScalarAsync<int>("select cast(123 as bigint)").ConfigureAwait(false);
        Assert.Equal(123, i);

        long j = await connection.ExecuteScalarAsync<long>("select 123").ConfigureAwait(false);
        Assert.Equal(123L, j);

        j = await connection.ExecuteScalarAsync<long>("select cast(123 as bigint)").ConfigureAwait(false);
        Assert.Equal(123L, j);

        int? k = await connection.ExecuteScalarAsync<int?>("select @i", new { i = default(int?) }).ConfigureAwait(false);
        Assert.Null(k);
    }

    [Fact]
    public async Task Issue346_QueryAsyncConvert() {
        int i = (await connection.QueryAsync<int>("Select Cast(123 as bigint)").ConfigureAwait(false)).First();
        Assert.Equal(123, i);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressionsAsync() {
        {
            var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2, Index = new Index() } };

            var p = new DynamicParameters(bob);
            p.Output(bob, b => b.PersonId);
            p.Output(bob, b => b.Occupation);
            p.Output(bob, b => b.NumberOfLegs);
            p.Output(bob, b => b.Address!.Name);
            p.Output(bob, b => b.Address!.PersonId);
            p.Output(bob, b => b.Address!.Index!.Id);

            await connection.ExecuteAsync(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
SET @AddressIndexId = '01088'", p).ConfigureAwait(false);

            Assert.Equal("grillmaster", bob.Occupation);
            Assert.Equal(2, bob.PersonId);
            Assert.Equal(1, bob.NumberOfLegs);
            Assert.Equal("bobs burgers", bob.Address.Name);
            Assert.Equal(2, bob.Address.PersonId);
            Assert.Equal("01088", bob.Address.Index.Id);
        }
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_ScalarAsync() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        var result = (int)(await connection.ExecuteScalarAsync(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p).ConfigureAwait(false))!;

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_Query_Default() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        var result = (await connection.QueryAsync<int>(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p).ConfigureAwait(false)).Single();

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_QueryFirst() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        var result = (await connection.QueryFirstAsync<int>(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p).ConfigureAwait(false));

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_Query_BufferedAsync() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        var result = (await connection.QueryAsync<int>(new CommandDefinition(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p, flags: CommandFlags.Buffered)).ConfigureAwait(false)).Single();

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_Query_NonBufferedAsync() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        var result = (await connection.QueryAsync<int>(new CommandDefinition(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p, flags: CommandFlags.None)).ConfigureAwait(false)).Single();

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task TestSupportForDynamicParametersOutputExpressions_QueryMultipleAsync() {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters(bob);
        p.Output(bob, b => b.PersonId);
        p.Output(bob, b => b.Occupation);
        p.Output(bob, b => b.NumberOfLegs);
        p.Output(bob, b => b.Address!.Name);
        p.Output(bob, b => b.Address!.PersonId);

        int x, y;
        using (var multi = await connection.QueryMultipleAsync(@"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
select 42
select 17
SET @AddressPersonId = @PersonId", p).ConfigureAwait(false)) {
            x = multi.ReadAsync<int>().Result.Single();
            y = multi.ReadAsync<int>().Result.Single();
        }

        Assert.Equal("grillmaster", bob.Occupation);
        Assert.Equal(2, bob.PersonId);
        Assert.Equal(1, bob.NumberOfLegs);
        Assert.Equal("bobs burgers", bob.Address.Name);
        Assert.Equal(2, bob.Address.PersonId);
        Assert.Equal(42, x);
        Assert.Equal(17, y);
    }

    [Fact]
    public async Task TestSubsequentQueriesSuccessAsync() {
        var data0 = (await connection.QueryAsync<AsyncFoo0>("select 1 as [Id] where 1 = 0").ConfigureAwait(false)).ToList();
        Assert.Empty(data0);

        var data1 = (await connection.QueryAsync<AsyncFoo1>(new CommandDefinition("select 1 as [Id] where 1 = 0", flags: CommandFlags.Buffered)).ConfigureAwait(false)).ToList();
        Assert.Empty(data1);

        var data2 = (await connection.QueryAsync<AsyncFoo2>(new CommandDefinition("select 1 as [Id] where 1 = 0", flags: CommandFlags.None)).ConfigureAwait(false)).ToList();
        Assert.Empty(data2);

        data0 = (await connection.QueryAsync<AsyncFoo0>("select 1 as [Id] where 1 = 0").ConfigureAwait(false)).ToList();
        Assert.Empty(data0);

        data1 = (await connection.QueryAsync<AsyncFoo1>(new CommandDefinition("select 1 as [Id] where 1 = 0", flags: CommandFlags.Buffered)).ConfigureAwait(false)).ToList();
        Assert.Empty(data1);

        data2 = (await connection.QueryAsync<AsyncFoo2>(new CommandDefinition("select 1 as [Id] where 1 = 0", flags: CommandFlags.None)).ConfigureAwait(false)).ToList();
        Assert.Empty(data2);
    }

    private class AsyncFoo0 { public int Id { get; set; } }

    private class AsyncFoo1 { public int Id { get; set; } }

    private class AsyncFoo2 { public int Id { get; set; } }

    [Fact]
    public async Task TestSchemaChangedViaFirstOrDefaultAsync() {
        await connection.ExecuteAsync("create table #dog(Age int, Name nvarchar(max)) insert #dog values(1, 'Alf')").ConfigureAwait(false);
        try {
            var d = await connection.QueryFirstOrDefaultAsync<Dog>("select * from #dog").ConfigureAwait(false);
            Assert.NotNull(d);
            Assert.Equal("Alf", d.Name);
            Assert.Equal(1, d.Age);
            connection.Execute("alter table #dog drop column Name");
            d = await connection.QueryFirstOrDefaultAsync<Dog>("select * from #dog").ConfigureAwait(false);
            Assert.NotNull(d);
            Assert.Null(d.Name);
            Assert.Equal(1, d.Age);
        }
        finally {
            await connection.ExecuteAsync("drop table #dog").ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task TestMultiMapArbitraryMapsAsync() {
        // please excuse the trite example, but it is easier to follow than a more real-world one
        const string createSql = @"
                create table #ReviewBoards (Id int, Name varchar(20), User1Id int, User2Id int, User3Id int, User4Id int, User5Id int, User6Id int, User7Id int, User8Id int, User9Id int)
                create table #Users (Id int, Name varchar(20))

                insert #Users values(1, 'User 1')
                insert #Users values(2, 'User 2')
                insert #Users values(3, 'User 3')
                insert #Users values(4, 'User 4')
                insert #Users values(5, 'User 5')
                insert #Users values(6, 'User 6')
                insert #Users values(7, 'User 7')
                insert #Users values(8, 'User 8')
                insert #Users values(9, 'User 9')

                insert #ReviewBoards values(1, 'Review Board 1', 1, 2, 3, 4, 5, 6, 7, 8, 9)
";
        await connection.ExecuteAsync(createSql).ConfigureAwait(false);
        try {
            const string sql = @"
                select 
                    rb.Id, rb.Name,
                    u1.*, u2.*, u3.*, u4.*, u5.*, u6.*, u7.*, u8.*, u9.*
                from #ReviewBoards rb
                    inner join #Users u1 on u1.Id = rb.User1Id
                    inner join #Users u2 on u2.Id = rb.User2Id
                    inner join #Users u3 on u3.Id = rb.User3Id
                    inner join #Users u4 on u4.Id = rb.User4Id
                    inner join #Users u5 on u5.Id = rb.User5Id
                    inner join #Users u6 on u6.Id = rb.User6Id
                    inner join #Users u7 on u7.Id = rb.User7Id
                    inner join #Users u8 on u8.Id = rb.User8Id
                    inner join #Users u9 on u9.Id = rb.User9Id
";

            var types = new[] { typeof(ReviewBoard), typeof(User), typeof(User), typeof(User), typeof(User), typeof(User), typeof(User), typeof(User), typeof(User), typeof(User) };

            Func<object[], ReviewBoard> mapper = (objects) =>
            {
                var board = (ReviewBoard)objects[0];
                board.User1 = (User)objects[1];
                board.User2 = (User)objects[2];
                board.User3 = (User)objects[3];
                board.User4 = (User)objects[4];
                board.User5 = (User)objects[5];
                board.User6 = (User)objects[6];
                board.User7 = (User)objects[7];
                board.User8 = (User)objects[8];
                board.User9 = (User)objects[9];
                return board;
            };

            var data = (await connection.QueryAsync<ReviewBoard>(sql, types, mapper).ConfigureAwait(false)).ToList();

            var p = data[0];
            Assert.Equal(1, p.Id);
            Assert.Equal("Review Board 1", p.Name);
            Assert.NotNull(p.User1);
            Assert.NotNull(p.User2);
            Assert.NotNull(p.User3);
            Assert.NotNull(p.User4);
            Assert.NotNull(p.User5);
            Assert.NotNull(p.User6);
            Assert.NotNull(p.User7);
            Assert.NotNull(p.User8);
            Assert.NotNull(p.User9);
            Assert.Equal(1, p.User1.Id);
            Assert.Equal(2, p.User2.Id);
            Assert.Equal(3, p.User3.Id);
            Assert.Equal(4, p.User4.Id);
            Assert.Equal(5, p.User5.Id);
            Assert.Equal(6, p.User6.Id);
            Assert.Equal(7, p.User7.Id);
            Assert.Equal(8, p.User8.Id);
            Assert.Equal(9, p.User9.Id);
            Assert.Equal("User 1", p.User1.Name);
            Assert.Equal("User 2", p.User2.Name);
            Assert.Equal("User 3", p.User3.Name);
            Assert.Equal("User 4", p.User4.Name);
            Assert.Equal("User 5", p.User5.Name);
            Assert.Equal("User 6", p.User6.Name);
            Assert.Equal("User 7", p.User7.Name);
            Assert.Equal("User 8", p.User8.Name);
            Assert.Equal("User 9", p.User9.Name);
        }
        finally {
            connection.Execute("drop table #Users drop table #ReviewBoards");
        }
    }

    [Fact]
    public async Task Issue157_ClosedReaderAsync() {
        var args = new { x = 42 };
        const string sql = "select 123 as [A], 'abc' as [B] where @x=42";
        var row = (await connection.QueryAsync<SomeType>(new CommandDefinition(
            sql, args, flags: CommandFlags.None)).ConfigureAwait(false)).Single();
        Assert.NotNull(row);
        Assert.Equal(123, row.A);
        Assert.Equal("abc", row.B);

        args = new { x = 5 };
        Assert.False((await connection.QueryAsync<SomeType>(new CommandDefinition(sql, args, flags: CommandFlags.None)).ConfigureAwait(false)).Any());
    }

    [Fact]
    public async Task TestAtEscaping() {
        var id = (await connection.QueryAsync<int>(@"
                declare @@Name int
                select @@Name = @Id+1
                select @@Name
                ", new Product { Id = 1 }).ConfigureAwait(false)).Single();
        Assert.Equal(2, id);
    }

    [Fact]
    public async Task Issue1281_DataReaderOutOfOrderAsync() {
        using var reader = await connection.ExecuteReaderAsync("Select 0, 1, 2").ConfigureAwait(false);
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Issue563_QueryAsyncShouldThrowException() {
        try {
            var data = (await connection.QueryAsync<int>("select 1 union all select 2; RAISERROR('after select', 16, 1);").ConfigureAwait(false)).ToList();
            Assert.Fail("Expected Exception");
        }
        catch (Exception ex) when (ex.GetType().Name == "SqlException" && ex.Message == "after select") { 
    // swallow only this 
}
    }
}

[Collection(NonParallelDefinition.Name)]
public abstract class AsyncQueryCacheTests<TProvider> : TestBase<TProvider> where TProvider : SqlServerDatabaseProvider {
    private readonly ITestOutputHelper _log;
    public AsyncQueryCacheTests(ITestOutputHelper log) => _log = log;
    private DbConnection? _marsConnection;
    private DbConnection MarsConnection => _marsConnection ??= Provider.GetOpenConnection(true);

    public override void Dispose() {
        _marsConnection?.Dispose();
        _marsConnection = null;
        base.Dispose();
    }

    [Fact]
    public void AssertNoCacheWorksForQueryMultiple() {
        const int a = 123, b = 456;
        var cmdDef = new CommandDefinition("select @a; select @b;", new {
            a,
            b
        }, commandType: CommandType.Text, flags: CommandFlags.NoCache);

        int c, d;
        SqlMapper.PurgeQueryCache();
        int before = SqlMapper.GetCachedSQLCount();
        using (var multi = MarsConnection.QueryMultiple(cmdDef)) {
            c = multi.Read<int>().Single();
            d = multi.Read<int>().Single();
        }
        int after = SqlMapper.GetCachedSQLCount();
        _log?.WriteLine($"before: {before}; after: {after}");
        // too brittle in concurrent tests to assert
        // Assert.Equal(0, before);
        // Assert.Equal(0, after);
        Assert.Equal(123, c);
        Assert.Equal(456, d);
    }

    [Fact]
    public async Task AssertNoCacheWorksForMultiMap() {
        const int a = 123, b = 456;
        var cmdDef = new CommandDefinition("select @a as a, @b as b;", new {
            a,
            b
        }, commandType: CommandType.Text, flags: CommandFlags.NoCache | CommandFlags.Buffered);

        SqlMapper.PurgeQueryCache();
        var before = SqlMapper.GetCachedSQLCount();
        Assert.Equal(0, before);

        await MarsConnection.QueryAsync<int, int, (int, int)>(cmdDef, splitOn: "b", map: (a, b) => (a, b));
        Assert.Equal(0, SqlMapper.GetCachedSQLCount());
    }

    [Fact]
    public async Task AssertNoCacheWorksForQueryAsync() {
        const int a = 123, b = 456;
        var cmdDef = new CommandDefinition("select @a as a, @b as b;", new {
            a,
            b
        }, commandType: CommandType.Text, flags: CommandFlags.NoCache | CommandFlags.Buffered);

        SqlMapper.PurgeQueryCache();
        var before = SqlMapper.GetCachedSQLCount();
        Assert.Equal(0, before);

        await MarsConnection.QueryAsync<(int, int)>(cmdDef);
        Assert.Equal(0, SqlMapper.GetCachedSQLCount());
    }*/
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