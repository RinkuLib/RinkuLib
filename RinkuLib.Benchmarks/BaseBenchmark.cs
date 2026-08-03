using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Benchmarks;

/// <summary>
/// The benchmark matrix has three kinds of cases. The first compares Dapper with Rinku using
/// equivalent database work. The second compares Rinku's own public execution routes. The third
/// measures Rinku features that do not have a useful Dapper equivalent. The Dapper comparisons are
/// laid out to mirror established ORM benchmark suites so the numbers are comparable to their
/// published results:
/// <list type="bullet">
/// <item>DapperLib/Dapper (benchmarks/Dapper.Tests.Performance) for the wide <c>Post</c> row and the rotating id.</item>
/// <item>FransBouma/RawDataAccessBencher for equal connection handling across libraries.</item>
/// <item>InfoTechBridge/OrmBenchmark for the single-row-repeated and bulk-set-fetch shapes.</item>
/// </list>
/// Fairness rests on four choices, applied identically to both libraries:
/// <list type="number">
/// <item>A wide 13-column row (varchar(max) text plus nine nullable ints) so materialization is a real cost.</item>
/// <item>5000 seeded rows with the queried id rotating each call, so no single hot row skews the cache.</item>
/// <item>One connection opened in setup and reused, so the run measures mapping, not pool rent/return.</item>
/// <item>A setup pass asserting Dapper and Rinku return identical results for every category.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BaseBenchmark : IAsyncDisposable {
    private const int RowCount = 5000;

    private DBFixture<SqlConnection> _fixture = null!;
    private SqlConnection cnn = null!;
    private BatchUpdateArgs[] _batchItems = [];

    private int _i;
    private bool _fixId;
    private int NextId() {
        if (_fixId)
            return 1;
        _i++;
        if (_i > RowCount)
            _i = 1;
        return _i;
    }

    private const string SelectPostSql = "SELECT * FROM Posts WHERE Id = @id";
    private const string SelectAllPostsSql = "SELECT * FROM Posts";
    private const string SelectComplexSql = "SELECT p.Id, p.Name, c.Id, c.Name, c.Description FROM Products p INNER JOIN Categories c ON p.CategoryId = c.Id WHERE p.Id = @id";
    private const string UpdateSql = "UPDATE Posts SET Counter1 = @val WHERE Id = @id";
    private const string InClauseSql = "SELECT * FROM Posts WHERE Id IN @ids";
    private const string InClauseSqlRinku = "SELECT * FROM Posts WHERE Id IN (@ids_X)";
    private const string CountSql = "SELECT COUNT(*) FROM Posts";
    private const string CountByIdSql = "SELECT COUNT(*) FROM Posts WHERE Id = @id";
    private const string ConditionalCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = ?@id";
    private const string SelectIdsSql = "SELECT Id FROM Posts";
    private const string DapperLiteralCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = {=id}";
    private const string RinkuLiteralCountSql = "SELECT COUNT(*) FROM Posts WHERE Id = @id_N";
    private const string SelectPostCommentsSql = @"
        SELECT p.Id, CAST(p.Id AS NVARCHAR(20)) AS Name,
               c.Id, c.Text AS Value
        FROM Posts p
        LEFT JOIN PostComments c ON c.PostId = p.Id
        ORDER BY p.Id, c.Id";
    private const string SelectPostCommentSetsSql = @"
        SELECT Id, CAST(Id AS NVARCHAR(20)) AS Name
        FROM Posts
        ORDER BY Id;
        SELECT PostId, Id, Text AS Title
        FROM PostComments
        ORDER BY PostId, Id";
    private const string MultiSql = "SELECT * FROM Posts WHERE Id = @a; SELECT * FROM Posts WHERE Id = @b";

    private static readonly QueryCommand QueryPostCmd = new(SelectPostSql);
    private static readonly QueryCommand QueryAllPostsCmd = new(SelectAllPostsSql);
    private static readonly QueryCommand QueryComplexCmd = new(SelectComplexSql);
    private static readonly QueryCommand ExecuteUpdateCmd = new(UpdateSql);
    private static readonly QueryCommand InClauseCmd = new(InClauseSqlRinku);
    private static readonly QueryCommand CountCmd = new(CountSql);
    private static readonly QueryCommand CountByIdCmd = new(CountByIdSql);
    private static readonly QueryCommand ConditionalCountCmd = new(ConditionalCountSql);
    private static readonly QueryCommand SelectIdsCmd = new(SelectIdsSql);
    private static readonly QueryCommand SelectPostCommentsCmd = new(SelectPostCommentsSql);
    private static readonly QueryCommand SelectPostCommentSetsCmd = new(SelectPostCommentSetsSql);
    private static readonly QueryCommand RinkuLiteralCountCmd = new(RinkuLiteralCountSql);
    private static readonly QueryCommand MultiCmd = new(MultiSql);

    [GlobalSetup]
    public Task Setup() => Setup(true);
    public async Task Setup(bool withValidate) {
        _fixture = new DBFixture<SqlConnection>();
        await _fixture.InitializeAsync();

        await using (var seed = _fixture.GetConnection()) {
            await seed.OpenAsync();

            async Task Exec(string sql) {
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            await Exec(@"
        CREATE TABLE Posts (
            Id INT IDENTITY PRIMARY KEY,
            Text VARCHAR(MAX) NOT NULL,
            CreationDate DATETIME NOT NULL,
            LastChangeDate DATETIME NOT NULL,
            Counter1 INT, Counter2 INT, Counter3 INT, Counter4 INT, Counter5 INT,
            Counter6 INT, Counter7 INT, Counter8 INT, Counter9 INT
        );

        CREATE TABLE Categories (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100),
            Description NVARCHAR(MAX)
        );

        CREATE TABLE Products (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100),
            CategoryId INT REFERENCES Categories(Id)
        );

        CREATE TABLE PostComments (
            Id INT NOT NULL PRIMARY KEY,
            PostId INT NOT NULL REFERENCES Posts(Id),
            Text NVARCHAR(100) NOT NULL
        );");

            await Exec($@"
        SET NOCOUNT ON;
        DECLARE @i INT = 0;
        WHILE @i < {RowCount} BEGIN
            INSERT INTO Posts (Text, CreationDate, LastChangeDate)
            VALUES (REPLICATE('x', 2000), GETDATE(), GETDATE());
            SET @i += 1;
        END");

            await Exec("INSERT INTO Categories (Id, Name, Description) VALUES (1, 'Electronics', 'Gadgets and stuff')");
            await Exec("INSERT INTO Products (Id, Name, CategoryId) VALUES (1, 'Laptop', 1)");
            await Exec($@"
        DECLARE @postId INT = 1;
        WHILE @postId <= {RowCount} BEGIN
            INSERT INTO PostComments (Id, PostId, Text)
            VALUES (@postId * 10 + 1, @postId, 'first'),
                   (@postId * 10 + 2, @postId, 'second'),
                   (@postId * 10 + 3, @postId, 'third');
            SET @postId += 1;
        END");
        }

        cnn = _fixture.GetConnection();
        await cnn.OpenAsync();
        _batchItems = Enumerable.Range(1, 64).Select(id => new BatchUpdateArgs(1, id)).ToArray();
        if (!withValidate)
            return;
        Console.WriteLine("--- Starting Full Equivalence Validation ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All comparison categories match ---");

        Console.WriteLine("--- Starting Full Equivalence Validation (second pass) ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All comparison categories match (second pass) ---");
    }

    private async Task Validate() {
        _fixId = true;
        try {
            var q1D = Dapper_QueryFirst();
            var q1R = Rinku_QueryT();
            var q1R2 = Rinku2_QueryT();
            if (q1D != q1R || q1D != q1R2)
                throw new Exception("1. Query one Sync: Results differ.");

            var q2D = Dapper_QueryFirstOrDefault();
            var q2R = Rinku_QueryOptionalT();
            var q2R2 = Rinku2_QueryOptionalT();
            if (q2D != q2R || q2D != q2R2)
                throw new Exception("2. Query one (or default) Sync: Results differ.");

            var q3D = Dapper_QuerySingle();
            var q3R = Rinku_QuerySingleT();
            var q3R2 = Rinku2_QuerySingleT();
            if (q3D != q3R || q3D != q3R2)
                throw new Exception("3. Query one (single) Sync: Results differ.");

            var q4D = await Dapper_QueryFirstAsync();
            var q4R = await Rinku_QueryTAsync();
            var q4R2 = await Rinku2_QueryTAsync();
            if (q4D != q4R || q4D != q4R2)
                throw new Exception("4. Query one Async: Results differ.");

            var q5D = await Dapper_QueryFirstOrDefaultAsync();
            var q5R = await Rinku_QueryOptionalTAsync();
            var q5R2 = await Rinku2_QueryOptionalTAsync();
            if (q5D != q5R || q5D != q5R2)
                throw new Exception("5. Query one (or default) Async: Results differ.");

            var q6D = await Dapper_QuerySingleAsync();
            var q6R = await Rinku_QuerySingleTAsync();
            var q6R2 = await Rinku2_QuerySingleTAsync();
            if (q6D != q6R || q6D != q6R2)
                throw new Exception("6. Query one (single) Async: Results differ.");

            var q7D = Dapper_QueryUnbuffered();
            var q7R = Rinku_QueryIEnumerable();
            var q7R2 = Rinku2_QueryIEnumerable();
            if (q7D != q7R || q7D != q7R2)
                throw new Exception("7. Query Sync (Stream): Sums differ.");

            var q8D = Dapper_QueryBuffered();
            var q8R = Rinku_QueryList();
            var q8R2 = Rinku2_QueryList();
            if (q8D.Count != q8R.Count || q8D.Count != q8R2.Count)
                throw new Exception("8. Query Buffered Sync: Collections differ.");
            for (var i = 0; i < q8D.Count; i++)
                if (q8D[i] != q8R[i] || q8D[i] != q8R2[i])
                    throw new Exception("8. Query Buffered Sync: Collections differ.");

            var q9D = await Dapper_QueryUnbufferedAsync();
            var q9R = await Rinku_StreamQueryAsync();
            var q9R2 = await Rinku2_StreamQueryAsync();
            if (q9D != q9R || q9D != q9R2)
                throw new Exception("9. Query Async (Stream): Sums differ.");

            var q10D = await Dapper_QueryAsyncBuffered();
            var q10R = await Rinku_QueryAsyncList();
            var q10R2 = await Rinku2_QueryAsyncList();
            if (q10D.Count != q10R.Count || q10D.Count != q10R2.Count)
                throw new Exception("10. Query Buffered Async: Collections differ.");
            for (var i = 0; i < q10D.Count; i++)
                if (q10D[i] != q10R[i] || q10D[i] != q10R2[i])
                    throw new Exception("10. Query Buffered Async: Collections differ.");

            var q11D = await Dapper_QueryAsyncDynamic();
            var q11R = await Rinku_QueryAsyncDynaObject();
            var q11R2 = await Rinku2_QueryAsyncDynaObject();
            if (q11D != q11R || q11D != q11R2)
                throw new Exception("11. Dynamic Async: Values differ.");

            var q12D = await Dapper_Complex();
            var q12R = await Rinku_Complex();
            var q12R2 = await Rinku2_Complex();
            if (q12D.Count != q12R.Count || q12D.Count != q12R2.Count)
                throw new Exception("12. Complex Mapping: Results differ.");
            for (var i = 0; i < q12D.Count; i++)
                if (q12D[i] != q12R[i] || q12D[i] != q12R2[i])
                    throw new Exception($"12. Complex Mapping: Results differ. Dapper={q12D[i].Id}/{q12D[i].Name}/{q12D[i].Category}; Rinku={q12R[i].Id}/{q12R[i].Name}/{q12R[i].Category}");

            var q13D = Dapper_Execute();
            var q13R = Rinku_Execute();
            var q13R2 = Rinku2_Execute();
            if (q13D != q13R || q13D != q13R2)
                throw new Exception("13. Execute Sync: Row counts differ.");

            var q14D = await Dapper_ExecuteAsync();
            var q14R = await Rinku_ExecuteAsync();
            var q14R2 = await Rinku2_ExecuteAsync();
            if (q14D != q14R || q14D != q14R2)
                throw new Exception("14. Execute Async: Row counts differ.");

            var q15D = await Dapper_InClause();
            var q15R = await Rinku_InClause();
            var q15R2 = await Rinku2_InClause();
            if (q15D != q15R || q15D != q15R2)
                throw new Exception("15. IN Clause: Results differ.");

            var q16D = await Dapper_Scalar();
            var q16R = await Rinku_Scalar();
            var q16R2 = await Rinku2_Scalar();
            if (q16D != q16R || q16D != q16R2)
                throw new Exception("16. Scalar: Values differ.");

            var q17D = await Dapper_ScalarSequence();
            var q17R = await Rinku_ScalarSequence();
            var q17R2 = await Rinku2_ScalarSequence();
            if (q17D.Count != q17R.Count || q17D.Count != q17R2.Count)
                throw new Exception("17. Scalar Sequence: Collections differ.");
            for (var i = 0; i < q17D.Count; i++)
                if (q17D[i] != q17R[i] || q17D[i] != q17R2[i])
                    throw new Exception("17. Scalar Sequence: Collections differ.");

            var q18D = await Dapper_MultiResultSet();
            var q18R = await Rinku_MultiResultSet();
            var q18R2 = await Rinku2_MultiResultSet();
            if (q18D != q18R || q18D != q18R2)
                throw new Exception("18. Multiple Result Sets: Sums differ.");

            if (await Rinku_FixedCount() != await Rinku_ConditionalCountWithoutId()
                || await Rinku_FixedCountById() != await Rinku_ConditionalCountWithId())
                throw new Exception("Conditional SQL: Results differ.");

            var groups = await SelectPostCommentsCmd.QueryAsync<List<PostGroup>>(cnn);
            if (groups.Count != RowCount || groups[0].Children.Count != 3 || groups[^1].Children.Count != 3)
                throw new Exception("Multi-row validation failed.");

            var expected = Rinku_OneToManyNative();
            var tuples = Rinku_OneToManyTuples();
            var dapper = Dapper_OneToManyMultiMap();
            var separate = await Rinku_OneToManySeparateResultSets();
            var separateDapper = await Dapper_OneToManySeparateResultSets();
            var ordered = await Rinku_OneToManySeparateResultSetsOrdered();
            var orderedDapper = await Dapper_OneToManySeparateResultSetsOrdered();
            if (!SameGroups(expected, tuples) || !SameGroups(expected, dapper))
                throw new Exception("Multi-row route validation failed.");
            var expectedTotal = GroupTotal(expected);
            if (expectedTotal != separate || expectedTotal != separateDapper || expectedTotal != ordered || expectedTotal != orderedDapper)
                throw new Exception("Separate result-set route validation failed.");

            if (Dapper_LiteralReplacement() != 1 || Rinku_NumericLiteral() != 1 || Rinku_ParameterizedCount() != 1)
                throw new Exception("Literal benchmark validation failed.");

            if (Dapper_BatchExecute() != _batchItems.Length || Rinku_BatchUseWith() != _batchItems.Length)
                throw new Exception("Batch execution validation failed.");

            var wasFixed = _fixId;
            _fixId = true;
            Post dapperParameters;
            Post builderParameters;
            try {
                dapperParameters = Dapper_DynamicParameters();
                builderParameters = Rinku_BuilderCommand();
            }
            finally {
                _fixId = wasFixed;
            }
            if (dapperParameters != builderParameters)
                throw new Exception("Dynamic parameter benchmark validation failed.");
        }
        finally {
            _fixId = false;
        }
    }

    private static int GroupTotal(IReadOnlyList<PostGroup> groups)
        => groups.Sum(group => group.Id + group.Children.Count);

    private static bool SameGroups(IReadOnlyList<PostGroup> left, IReadOnlyList<PostGroup> right) {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++) {
            var leftGroup = left[i];
            var rightGroup = right[i];
            if (leftGroup.Id != rightGroup.Id || leftGroup.Name != rightGroup.Name || leftGroup.Children.Count != rightGroup.Children.Count)
                return false;
            for (var j = 0; j < leftGroup.Children.Count; j++) {
                var leftChild = leftGroup.Children[j];
                var rightChild = rightGroup.Children[j];
                if (leftChild.Id != rightChild.Id || leftChild.Value != rightChild.Value)
                    return false;
            }
        }
        return true;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Query one Sync")]
    public Post Dapper_QueryFirst() => cnn.QueryFirst<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Sync")]
    public Post Rinku_QueryT() => QueryPostCmd.Query<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Sync")]
    public Post Rinku2_QueryT() => cnn.Query<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (or default) Sync")]
    public Post? Dapper_QueryFirstOrDefault() => cnn.QueryFirstOrDefault<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? Rinku_QueryOptionalT() => QueryPostCmd.Query<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Sync")]
    public Post? Rinku2_QueryOptionalT() => cnn.Query<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single) Sync")]
    public Post Dapper_QuerySingle() => cnn.QuerySingle<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Sync")]
    public Post Rinku_QuerySingleT() => QueryPostCmd.Query<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Sync")]
    public Post Rinku2_QuerySingleT() => cnn.Query<Single<Post>>(SelectPostSql, new { id = NextId() });


    [Benchmark(Baseline = true), BenchmarkCategory("Query one Async")]
    public async Task<Post> Dapper_QueryFirstAsync() => await cnn.QueryFirstAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Async")]
    public async Task<Post> Rinku_QueryTAsync() => await QueryPostCmd.QueryAsync<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one Async")]
    public async Task<Post> Rinku2_QueryTAsync() => await cnn.QueryAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> Dapper_QueryFirstOrDefaultAsync() => await cnn.QueryFirstOrDefaultAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> Rinku_QueryOptionalTAsync() => await QueryPostCmd.QueryAsync<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (or default) Async")]
    public async Task<Post?> Rinku2_QueryOptionalTAsync() => await cnn.QueryAsync<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> Dapper_QuerySingleAsync() => await cnn.QuerySingleAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> Rinku_QuerySingleTAsync() => await QueryPostCmd.QueryAsync<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Query one (single) Async")]
    public async Task<Post> Rinku2_QuerySingleTAsync() => await cnn.QueryAsync<Single<Post>>(SelectPostSql, new { id = NextId() });


    [Benchmark(Baseline = true), BenchmarkCategory("Query Sync (Stream)")]
    public int Dapper_QueryUnbuffered() {
        var items = cnn.Query<Post>(SelectAllPostsSql, buffered: false);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Sync (Stream)")]
    public int Rinku_QueryIEnumerable() {
        var items = QueryAllPostsCmd.Query<IEnumerable<Post>>(cnn);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }
    [Benchmark, BenchmarkCategory("Query Sync (Stream)")]
    public int Rinku2_QueryIEnumerable() {
        var items = cnn.Query<IEnumerable<Post>>(SelectAllPostsSql);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("Query Buffered Sync")]
    public List<Post> Dapper_QueryBuffered() => cnn.Query<Post>(SelectAllPostsSql, buffered: true).AsList();

    [Benchmark, BenchmarkCategory("Query Buffered Sync")]
    public List<Post> Rinku_QueryList() => QueryAllPostsCmd.Query<List<Post>>(cnn);

    [Benchmark, BenchmarkCategory("Query Buffered Sync")]
    public List<Post> Rinku2_QueryList() => cnn.Query<List<Post>>(SelectAllPostsSql);


    [Benchmark(Baseline = true), BenchmarkCategory("Query Async (Stream)")]
    public async Task<int> Dapper_QueryUnbufferedAsync() {
        var items = cnn.QueryUnbufferedAsync<Post>(SelectAllPostsSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Async (Stream)")]
    public async Task<int> Rinku_StreamQueryAsync() {
        var items = QueryAllPostsCmd.StreamQueryAsync<Post>(cnn);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("Query Async (Stream)")]
    public async Task<int> Rinku2_StreamQueryAsync() {
        var items = cnn.StreamQueryAsync<Post>(SelectAllPostsSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("Query Buffered Async")]
    public async Task<List<Post>> Dapper_QueryAsyncBuffered() => (await cnn.QueryAsync<Post>(SelectAllPostsSql, param: null)).AsList();

    [Benchmark, BenchmarkCategory("Query Buffered Async")]
    public Task<List<Post>> Rinku_QueryAsyncList() => QueryAllPostsCmd.QueryAsync<List<Post>>(cnn);

    [Benchmark, BenchmarkCategory("Query Buffered Async")]
    public Task<List<Post>> Rinku2_QueryAsyncList() => cnn.QueryAsync<List<Post>>(SelectAllPostsSql);


    [Benchmark(Baseline = true), BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Dapper_QueryAsyncDynamic() {
        var row = await cnn.QueryFirstAsync(SelectPostSql, new { id = NextId() });
        return ((int)row.Id, (string?)row.Text, (DateTime)row.CreationDate, (int?)row.Counter1);
    }

    [Benchmark, BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Rinku_QueryAsyncDynaObject() {
        var row = await QueryPostCmd.QueryAsync<DynaObject>(cnn, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }
    [Benchmark, BenchmarkCategory("Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Rinku2_QueryAsyncDynaObject() {
        var row = await cnn.QueryAsync<DynaObject>(SelectPostSql, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await cnn.QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("Complex Mapping")]
    public Task<List<Product>> Rinku_Complex() => QueryComplexCmd.QueryAsync<List<Product>>(cnn, new { id = 1 });

    [Benchmark, BenchmarkCategory("Complex Mapping")]
    public Task<List<Product>> Rinku2_Complex() => cnn.QueryAsync<List<Product>>(SelectComplexSql, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("Execute Sync")]
    public int Dapper_Execute() => cnn.Execute(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Sync")]
    public int Rinku_Execute() => ExecuteUpdateCmd.Execute(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Sync")]
    public int Rinku2_Execute() => cnn.Execute(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Async")]
    public Task<int> Rinku_ExecuteAsync() => ExecuteUpdateCmd.ExecuteAsync(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("Execute Async")]
    public Task<int> Rinku2_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("IN Clause")]
    public async Task<int> Dapper_InClause() {
        var items = cnn.QueryUnbufferedAsync<Post>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("IN Clause")]
    public async Task<int> Rinku_InClause() {
        var items = InClauseCmd.StreamQueryAsync<Post>(cnn, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("IN Clause")]
    public async Task<int> Rinku2_InClause() {
        var items = cnn.StreamQueryAsync<Post>(InClauseSqlRinku, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar Async")]
    public Task<int> Dapper_Scalar() => cnn.ExecuteScalarAsync<int>(CountSql, param: null);

    [Benchmark, BenchmarkCategory("Scalar Async")]
    public Task<int> Rinku_Scalar() => CountCmd.QueryAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("Scalar Async")]
    public Task<int> Rinku2_Scalar() => cnn.ExecuteScalarAsync<int>(CountSql);

    [Benchmark(Baseline = true), BenchmarkCategory("Scalar Sequence Async")]
    public async Task<List<int>> Dapper_ScalarSequence() => (await cnn.QueryAsync<int>(SelectIdsSql, param: null)).AsList();

    [Benchmark, BenchmarkCategory("Scalar Sequence Async")]
    public Task<List<int>> Rinku_ScalarSequence() => SelectIdsCmd.QueryAsync<List<int>>(cnn);

    [Benchmark, BenchmarkCategory("Scalar Sequence Async")]
    public Task<List<int>> Rinku2_ScalarSequence() => cnn.QueryAsync<List<int>>(SelectIdsSql);
    
    [Benchmark(Baseline = true), BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> Dapper_MultiResultSet() {
        using var grid = await cnn.QueryMultipleAsync(MultiSql, new { a = NextId(), b = NextId() });
        var p1 = await grid.ReadFirstAsync<Post>();
        var p2 = await grid.ReadFirstAsync<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> Rinku_MultiResultSet() {
        using var multi = await MultiCmd.ExecuteMultiReaderAsync(cnn, out var cmd, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        cmd.Dispose();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("Multiple Result Sets Async")]
    public async Task<int> Rinku2_MultiResultSet() {
        using var multi = await cnn.ExecuteMultiReaderAsync(MultiSql, out var cmd, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        cmd.Dispose();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Conditional SQL without parameter")]
    public Task<int> Rinku_FixedCount() => CountCmd.QueryAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("Conditional SQL without parameter")]
    public Task<int> Rinku_ConditionalCountWithoutId()
        => ConditionalCountCmd.QueryAsync<int>(cnn, new { id = (int?)null });

    [Benchmark(Baseline = true), BenchmarkCategory("Conditional SQL with parameter")]
    public Task<int> Rinku_FixedCountById() => CountByIdCmd.QueryAsync<int>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Conditional SQL with parameter")]
    public Task<int> Rinku_ConditionalCountWithId()
        => ConditionalCountCmd.QueryAsync<int>(cnn, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Literal replacement")]
    public int Dapper_LiteralReplacement() => cnn.QueryFirst<int>(DapperLiteralCountSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Literal replacement")]
    public int Rinku_NumericLiteral() => RinkuLiteralCountCmd.Query<int>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("Literal replacement")]
    public int Rinku_ParameterizedCount() => CountByIdCmd.Query<int>(cnn, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("Manually added parameters")]
    public Post Dapper_DynamicParameters() {
        var parameters = new DynamicParameters();
        parameters.Add("id", NextId());
        return cnn.QueryFirst<Post>(SelectPostSql, parameters);
    }

    [Benchmark, BenchmarkCategory("Manually added parameters")]
    public Post Rinku_BuilderCommand() {
        var builder = QueryPostCmd.StartBuilder();
        builder.Use("@id", NextId());
        return builder.Query<Post>(cnn);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Batch execution")]
    public int Dapper_BatchExecute() => SqlMapper.Execute(cnn, UpdateSql, _batchItems);

    [Benchmark, BenchmarkCategory("Batch execution")]
    public int Rinku_BatchUseWith() {
        using var command = cnn.CreateCommand();
        var builder = ExecuteUpdateCmd.StartBuilder(command);
        var affected = 0;
        foreach (var item in _batchItems) {
            builder.UseWith(item);
            affected += builder.Execute();
        }
        return affected;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> Dapper_OneToManyMultiMap() {
        var groups = new List<PostGroup>();
        PostGroup? current = null;
        var rows = cnn.Query<PostMultiMapParent, PostMultiMapChild, PostMultiMapParent>(
            SelectPostCommentsSql,
            (parent, child) => {
                if (current is null || current.Id != parent.Id) {
                    current = new PostGroup(parent.Id, parent.Name, []);
                    groups.Add(current);
                }
                current.Children.Add(child);
                return parent;
            },
            splitOn: "Id",
            buffered: false);
        foreach (var _ in rows) { }
        return groups;
    }

    [Benchmark, BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> Rinku_OneToManyTuples() {
        var rows = SelectPostCommentsCmd.Query<IEnumerable<(PostMultiMapParent Parent, PostMultiMapChild Child)>>(cnn);
        var groups = new List<PostGroup>();
        PostGroup? current = null;
        foreach (var (Parent, Child) in rows) {
            if (current is null || current.Id != Parent.Id) {
                current = new PostGroup(Parent.Id, Parent.Name, []);
                groups.Add(current);
            }
            current.Children.Add(Child);
        }
        return groups;
    }

    [Benchmark, BenchmarkCategory("One-to-many fold")]
    public List<PostGroup> Rinku_OneToManyNative() {
        return SelectPostCommentsCmd.Query<List<PostGroup>>(cnn);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many separate result sets")]
    public async Task<int> Dapper_OneToManySeparateResultSets() {
        using var multi = await cnn.QueryMultipleAsync(SelectPostCommentSetsSql);
        var parents = (await multi.ReadAsync<PostSetParent>()).AsList();
        var children = multi.ReadUnbufferedAsync<PostSetChild>();
        var byId = parents.ToDictionary(parent => parent.Id);
        await foreach (var child in children)
            byId[child.PostId].Children.Add(child);
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark, BenchmarkCategory("One-to-many separate result sets")]
    public async Task<int> Rinku_OneToManySeparateResultSets() {
        using var multi = await SelectPostCommentSetsCmd.ExecuteMultiReaderAsync(cnn, out var cmd);
        var parents = await multi.QueryAsync<List<PostSetParent>>();
        var children = await multi.QueryAsync<IEnumerable<PostSetChild>>();
        var byId = parents.ToDictionary(parent => parent.Id);
        foreach (var child in children)
            byId[child.PostId].Children.Add(child);
        cmd.Dispose();
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("One-to-many separate result sets (ordered)")]
    public async Task<int> Dapper_OneToManySeparateResultSetsOrdered() {
        using var multi = await cnn.QueryMultipleAsync(SelectPostCommentSetsSql);
        var parents = (await multi.ReadAsync<PostSetParent>()).AsList();
        await using var children = multi.ReadUnbufferedAsync<PostSetChild>().GetAsyncEnumerator();
        var more = await children.MoveNextAsync();
        foreach (var parent in parents) {
            while (more && children.Current.PostId == parent.Id) {
                parent.Children.Add(children.Current);
                more = await children.MoveNextAsync();
            }
        }
        if (more)
            throw new Exception("Ordered Dapper children were not fully consumed.");
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [Benchmark, BenchmarkCategory("One-to-many separate result sets (ordered)")]
    public async Task<int> Rinku_OneToManySeparateResultSetsOrdered() {
        using var multi = await SelectPostCommentSetsCmd.ExecuteMultiReaderAsync(cnn, out var cmd);
        var parents = await multi.QueryAsync<List<PostSetParent>>();
        var children = await multi.QueryAsync<IEnumerable<PostSetChild>>();
        using var enumerator = children.GetEnumerator();
        var more = enumerator.MoveNext();
        foreach (var parent in parents) {
            while (more && enumerator.Current.PostId == parent.Id) {
                parent.Children.Add(enumerator.Current);
                more = enumerator.MoveNext();
            }
        }
        if (more)
            throw new Exception("Ordered Rinku children were not fully consumed.");
        cmd.Dispose();
        return parents.Sum(parent => parent.Id + parent.Children.Count);
    }

    [GlobalCleanup]
    public async ValueTask Cleanup() => await DisposeAsync();

    public async ValueTask DisposeAsync() {
        if (cnn is not null)
            await cnn.DisposeAsync();
        if (_fixture is not null)
            await _fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed record BatchUpdateArgs(int Val, int Id);

public sealed record PostGroup([GroupKey] int Id, string Name, List<PostMultiMapChild> Children) : IDbReadable;

public sealed record PostMultiMapChild([AltSkippingSegments("Id", 2), AbortOnNull] int Id, [AltSkippingSegments("Value", 2)] string Value) : IDbReadable;

public sealed class PostMultiMapParent : IDbReadable {
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}

public sealed class PostSetParent : IDbReadable {
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<PostSetChild> Children { get; } = [];
}

public sealed record PostSetChild([AbortOnNull] int PostId, [AbortOnNull] int Id, string Title) : IDbReadable;

/// <summary>
/// The wide row the single-row and bulk benchmarks map, matching the Dapper suite's Post: a
/// varchar(max) text column and nine nullable ints, so materialization exercises varied types and
/// the null path. A positional record, so value equality drives the setup validation for free.
/// </summary>
public record Post(
    int Id,
    string? Text,
    DateTime CreationDate,
    DateTime LastChangeDate,
    int? Counter1,
    int? Counter2,
    int? Counter3,
    int? Counter4,
    int? Counter5,
    int? Counter6,
    int? Counter7,
    int? Counter8,
    int? Counter9) {
    public int Sum() => Id + (Text?.Length ?? 0)
        + (Counter1 ?? 0) + (Counter2 ?? 0) + (Counter3 ?? 0) + (Counter4 ?? 0) + (Counter5 ?? 0)
        + (Counter6 ?? 0) + (Counter7 ?? 0) + (Counter8 ?? 0) + (Counter9 ?? 0);
}

public class Product {
    public int Id { get; set; }
    public string? Name { get; set; }
    public Category? Category { get; set; }
    public static bool operator ==(Product? p1, Product? p2) {
        if (ReferenceEquals(p1, p2))
            return true;
        if (p1 is null || p2 is null)
            return false;

        return p1.Id == p2.Id &&
               p1.Name == p2.Name &&
               p1.Category == p2.Category;
    }

    public static bool operator !=(Product? p1, Product? p2) => !(p1 == p2);

    public override bool Equals(object? obj) => obj is Product other && this == other;

    public override int GetHashCode() => HashCode.Combine(Id, Name, Category);
}

public record class Category(
    [AltSkippingSegments("Id", 2)] int Id,
    [AltSkippingSegments("Name", 2)] string? Name,
    [AltSkippingSegments("Description", 2)] string? Description) : IDbReadable;
