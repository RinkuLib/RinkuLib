using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Benchmarks;

/// <summary>
/// Dapper vs Rinku, laid out to mirror the established ORM benchmark suites so the numbers are
/// comparable to their published results:
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
    private const string SelectComplexSqlRinku = "SELECT p.Id, p.Name, c.Id AS CategoryId, c.Name AS CategoryName, c.Description AS CategoryDescription FROM Products p INNER JOIN Categories c ON p.CategoryId = c.Id WHERE p.Id = @id";
    private const string UpdateSql = "UPDATE Posts SET Counter1 = @val WHERE Id = @id";
    private const string InClauseSql = "SELECT * FROM Posts WHERE Id IN @ids";
    private const string InClauseSqlRinku = "SELECT * FROM Posts WHERE Id IN (@ids_X)";
    private const string CountSql = "SELECT COUNT(*) FROM Posts";
    private const string SelectIdsSql = "SELECT Id FROM Posts";
    private const string MultiSql = "SELECT * FROM Posts WHERE Id = @a; SELECT * FROM Posts WHERE Id = @b";

    private static readonly QueryCommand QueryPostCmd = new(SelectPostSql);
    private static readonly QueryCommand QueryAllPostsCmd = new(SelectAllPostsSql);
    private static readonly QueryCommand QueryComplexCmd = new(SelectComplexSqlRinku);
    private static readonly QueryCommand ExecuteUpdateCmd = new(UpdateSql);
    private static readonly QueryCommand InClauseCmd = new(InClauseSqlRinku);
    private static readonly QueryCommand CountCmd = new(CountSql);
    private static readonly QueryCommand SelectIdsCmd = new(SelectIdsSql);
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
        }

        cnn = _fixture.GetConnection();
        await cnn.OpenAsync();
        if (!withValidate)
            return;
        Console.WriteLine("--- Starting Full Equivalence Validation ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All 18 Categories Match ---");

        Console.WriteLine("--- Starting Full Equivalence Validation (second pass) ---");
        await Validate();
        Console.WriteLine("--- Validation Passed: All 18 Categories Match (second pass) ---");
    }

    private async Task Validate() {
        _fixId = true;
        try {
            var q1D = Dapper_QueryFirst();
            var q1R = Rinku_QueryT();
            if (q1D != q1R)
                throw new Exception("1. Query one Sync: Results differ.");

            var q2D = Dapper_QueryFirstOrDefault();
            var q2R = Rinku_QueryOptionalT();
            if (q2D != q2R)
                throw new Exception("2. Query one (or default) Sync: Results differ.");

            var q3D = Dapper_QuerySingle();
            var q3R = Rinku_QuerySingleT();
            if (q3D != q3R)
                throw new Exception("3. Query one (single) Sync: Results differ.");

            var q4D = await Dapper_QueryFirstAsync();
            var q4R = await Rinku_QueryTAsync();
            if (q4D != q4R)
                throw new Exception("4. Query one Async: Results differ.");

            var q5D = await Dapper_QueryFirstOrDefaultAsync();
            var q5R = await Rinku_QueryOptionalTAsync();
            if (q5D != q5R)
                throw new Exception("5. Query one (or default) Async: Results differ.");

            var q6D = await Dapper_QuerySingleAsync();
            var q6R = await Rinku_QuerySingleTAsync();
            if (q6D != q6R)
                throw new Exception("6. Query one (single) Async: Results differ.");

            var q7D = Dapper_QueryUnbuffered();
            var q7R = Rinku_QueryIEnumerable();
            if (q7D != q7R)
                throw new Exception("7. Query Sync (Stream): Sums differ.");

            var q8D = Dapper_QueryBuffered();
            var q8R = Rinku_QueryList();
            if (q8D.Count != q8R.Count)
                throw new Exception("8. Query Buffered Sync: Collections differ.");
            for (var i = 0; i < q8D.Count; i++)
                if (q8D[i] != q8R[i])
                    throw new Exception("8. Query Buffered Sync: Collections differ.");

            var q9D = await Dapper_QueryUnbufferedAsync();
            var q9R = await Rinku_StreamQueryAsync();
            if (q9D != q9R)
                throw new Exception("9. Query Async (Stream): Sums differ.");

            var q10D = await Dapper_QueryAsyncBuffered();
            var q10R = await Rinku_QueryAsyncList();
            if (q10D.Count != q10R.Count)
                throw new Exception("10. Query Buffered Async: Collections differ.");
            for (var i = 0; i < q10D.Count; i++)
                if (q10D[i] != q10R[i])
                    throw new Exception("10. Query Buffered Async: Collections differ.");

            var q11D = await Dapper_QueryAsyncDynamic();
            var q11R = await Rinku_QueryAsyncDynaObject();
            if (q11D != q11R)
                throw new Exception("11. Dynamic Async: Values differ.");

            var q12D = await Dapper_Complex();
            var q12R = await Rinku_Complex();
            if (q12D.Count != q12R.Count)
                throw new Exception("12. Complex Mapping: Results differ.");
            for (var i = 0; i < q12D.Count; i++)
                if (q12D[i] != q12R[i])
                    throw new Exception("12. Complex Mapping: Results differ.");

            var q13D = Dapper_Execute();
            var q13R = Rinku_Execute();
            if (q13D != q13R)
                throw new Exception("13. Execute Sync: Row counts differ.");

            var q14D = await Dapper_ExecuteAsync();
            var q14R = await Rinku_ExecuteAsync();
            if (q14D != q14R)
                throw new Exception("14. Execute Async: Row counts differ.");

            var q15D = await Dapper_InClause();
            var q15R = await Rinku_InClause();
            if (q15D != q15R)
                throw new Exception("15. IN Clause: Results differ.");

            var q16D = await Dapper_Scalar();
            var q16R = await Rinku_Scalar();
            if (q16D != q16R)
                throw new Exception("16. Scalar: Values differ.");

            var q17D = await Dapper_ScalarSequence();
            var q17R = await Rinku_ScalarSequence();
            if (q17D.Count != q17R.Count)
                throw new Exception("17. Scalar Sequence: Collections differ.");
            for (var i = 0; i < q17D.Count; i++)
                if (q17D[i] != q17R[i])
                    throw new Exception("17. Scalar Sequence: Collections differ.");

            var q18D = await Dapper_MultiResultSet();
            var q18R = await Rinku_MultiResultSet();
            if (q18D != q18R)
                throw new Exception("18. Multiple Result Sets: Sums differ.");
        }
        finally {
            _fixId = false;
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("01. Query one Sync")]
    public Post Dapper_QueryFirst() => cnn.QueryFirst<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("01. Query one Sync")]
    public Post Rinku_QueryT() => QueryPostCmd.Query<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("01. Query one Sync")]
    public Post Rinku2_QueryT() => cnn.Query<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("02. Query one (or default) Sync")]
    public Post? Dapper_QueryFirstOrDefault() => cnn.QueryFirstOrDefault<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("02. Query one (or default) Sync")]
    public Post? Rinku_QueryOptionalT() => QueryPostCmd.Query<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("02. Query one (or default) Sync")]
    public Post? Rinku2_QueryOptionalT() => cnn.Query<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("03. Query one (single) Sync")]
    public Post Dapper_QuerySingle() => cnn.QuerySingle<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("03. Query one (single) Sync")]
    public Post Rinku_QuerySingleT() => QueryPostCmd.Query<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("03. Query one (single) Sync")]
    public Post Rinku2_QuerySingleT() => cnn.Query<Single<Post>>(SelectPostSql, new { id = NextId() });


    [Benchmark(Baseline = true), BenchmarkCategory("04. Query one Async")]
    public async Task<Post> Dapper_QueryFirstAsync() => await cnn.QueryFirstAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("04. Query one Async")]
    public async Task<Post> Rinku_QueryTAsync() => await QueryPostCmd.QueryAsync<Post>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("04. Query one Async")]
    public async Task<Post> Rinku2_QueryTAsync() => await cnn.QueryAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("05. Query one (or default) Async")]
    public async Task<Post?> Dapper_QueryFirstOrDefaultAsync() => await cnn.QueryFirstOrDefaultAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("05. Query one (or default) Async")]
    public async Task<Post?> Rinku_QueryOptionalTAsync() => await QueryPostCmd.QueryAsync<Optional<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("05. Query one (or default) Async")]
    public async Task<Post?> Rinku2_QueryOptionalTAsync() => await cnn.QueryAsync<Optional<Post>>(SelectPostSql, new { id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("06. Query one (single) Async")]
    public async Task<Post> Dapper_QuerySingleAsync() => await cnn.QuerySingleAsync<Post>(SelectPostSql, new { id = NextId() });

    [Benchmark, BenchmarkCategory("06. Query one (single) Async")]
    public async Task<Post> Rinku_QuerySingleTAsync() => await QueryPostCmd.QueryAsync<Single<Post>>(cnn, new { id = NextId() });

    [Benchmark, BenchmarkCategory("06. Query one (single) Async")]
    public async Task<Post> Rinku2_QuerySingleTAsync() => await cnn.QueryAsync<Single<Post>>(SelectPostSql, new { id = NextId() });


    [Benchmark(Baseline = true), BenchmarkCategory("07. Query Sync (Stream)")]
    public int Dapper_QueryUnbuffered() {
        var items = cnn.Query<Post>(SelectAllPostsSql, buffered: false);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("07. Query Sync (Stream)")]
    public int Rinku_QueryIEnumerable() {
        var items = QueryAllPostsCmd.Query<IEnumerable<Post>>(cnn);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }
    [Benchmark, BenchmarkCategory("07. Query Sync (Stream)")]
    public int Rinku2_QueryIEnumerable() {
        var items = cnn.Query<IEnumerable<Post>>(SelectAllPostsSql);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("08. Query Buffered Sync")]
    public List<Post> Dapper_QueryBuffered() => cnn.Query<Post>(SelectAllPostsSql, buffered: true).AsList();

    [Benchmark, BenchmarkCategory("08. Query Buffered Sync")]
    public List<Post> Rinku_QueryList() => QueryAllPostsCmd.Query<List<Post>>(cnn);

    [Benchmark, BenchmarkCategory("08. Query Buffered Sync")]
    public List<Post> Rinku2_QueryList() => cnn.Query<List<Post>>(SelectAllPostsSql);


    [Benchmark(Baseline = true), BenchmarkCategory("09. Query Async (Stream)")]
    public async Task<int> Dapper_QueryUnbufferedAsync() {
        var items = cnn.QueryUnbufferedAsync<Post>(SelectAllPostsSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("09. Query Async (Stream)")]
    public async Task<int> Rinku_StreamQueryAsync() {
        var items = QueryAllPostsCmd.StreamQueryAsync<Post>(cnn);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("09. Query Async (Stream)")]
    public async Task<int> Rinku2_StreamQueryAsync() {
        var items = cnn.StreamQueryAsync<Post>(SelectAllPostsSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("10. Query Buffered Async")]
    public async Task<List<Post>> Dapper_QueryAsyncBuffered() => (await cnn.QueryAsync<Post>(SelectAllPostsSql, param: null)).AsList();

    [Benchmark, BenchmarkCategory("10. Query Buffered Async")]
    public Task<List<Post>> Rinku_QueryAsyncList() => QueryAllPostsCmd.QueryAsync<List<Post>>(cnn);

    [Benchmark, BenchmarkCategory("10. Query Buffered Async")]
    public Task<List<Post>> Rinku2_QueryAsyncList() => cnn.QueryAsync<List<Post>>(SelectAllPostsSql);


    [Benchmark(Baseline = true), BenchmarkCategory("11. Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Dapper_QueryAsyncDynamic() {
        var row = await cnn.QueryFirstAsync(SelectPostSql, new { id = NextId() });
        return ((int)row.Id, (string?)row.Text, (DateTime)row.CreationDate, (int?)row.Counter1);
    }

    [Benchmark, BenchmarkCategory("11. Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Rinku_QueryAsyncDynaObject() {
        var row = await QueryPostCmd.QueryAsync<DynaObject>(cnn, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }
    [Benchmark, BenchmarkCategory("11. Dynamic Async")]
    public async Task<(int, string?, DateTime, int?)> Rinku2_QueryAsyncDynaObject() {
        var row = await cnn.QueryAsync<DynaObject>(SelectPostSql, new { id = NextId() });
        return (row.Get<int>("Id"), row.Get<string>("Text"), row.Get<DateTime>("CreationDate"), row.Get<int?>("Counter1"));
    }

    [Benchmark(Baseline = true), BenchmarkCategory("12. Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await cnn.QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("12. Complex Mapping")]
    public Task<List<Product>> Rinku_Complex() => QueryComplexCmd.QueryAsync<List<Product>>(cnn, new { id = 1 });

    [Benchmark, BenchmarkCategory("12. Complex Mapping")]
    public Task<List<Product>> Rinku2_Complex() => cnn.QueryAsync<List<Product>>(SelectComplexSqlRinku, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("13. Execute Sync")]
    public int Dapper_Execute() => cnn.Execute(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("13. Execute Sync")]
    public int Rinku_Execute() => ExecuteUpdateCmd.Execute(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("13. Execute Sync")]
    public int Rinku2_Execute() => cnn.Execute(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("14. Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, param: new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("14. Execute Async")]
    public Task<int> Rinku_ExecuteAsync() => ExecuteUpdateCmd.ExecuteAsync(cnn, new { val = 1, id = NextId() });

    [Benchmark, BenchmarkCategory("14. Execute Async")]
    public Task<int> Rinku2_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, new { val = 1, id = NextId() });

    [Benchmark(Baseline = true), BenchmarkCategory("15. IN Clause")]
    public async Task<int> Dapper_InClause() {
        var items = cnn.QueryUnbufferedAsync<Post>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("15. IN Clause")]
    public async Task<int> Rinku_InClause() {
        var items = InClauseCmd.StreamQueryAsync<Post>(cnn, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("15. IN Clause")]
    public async Task<int> Rinku2_InClause() {
        var items = cnn.StreamQueryAsync<Post>(InClauseSqlRinku, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("16. Scalar Async")]
    public Task<int> Dapper_Scalar() => cnn.ExecuteScalarAsync<int>(CountSql, param: null);

    [Benchmark, BenchmarkCategory("16. Scalar Async")]
    public Task<int> Rinku_Scalar() => CountCmd.QueryAsync<int>(cnn);

    [Benchmark, BenchmarkCategory("16. Scalar Async")]
    public Task<int> Rinku2_Scalar() => cnn.ExecuteScalarAsync<int>(CountSql);

    [Benchmark(Baseline = true), BenchmarkCategory("17. Scalar Sequence Async")]
    public async Task<List<int>> Dapper_ScalarSequence() => (await cnn.QueryAsync<int>(SelectIdsSql, param: null)).AsList();

    [Benchmark, BenchmarkCategory("17. Scalar Sequence Async")]
    public Task<List<int>> Rinku_ScalarSequence() => SelectIdsCmd.QueryAsync<List<int>>(cnn);

    [Benchmark, BenchmarkCategory("17. Scalar Sequence Async")]
    public Task<List<int>> Rinku2_ScalarSequence() => cnn.QueryAsync<List<int>>(SelectIdsSql);
    
    [Benchmark(Baseline = true), BenchmarkCategory("18. Multiple Result Sets Async")]
    public async Task<int> Dapper_MultiResultSet() {
        using var grid = await cnn.QueryMultipleAsync(MultiSql, new { a = NextId(), b = NextId() });
        var p1 = await grid.ReadFirstAsync<Post>();
        var p2 = await grid.ReadFirstAsync<Post>();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("18. Multiple Result Sets Async")]
    public async Task<int> Rinku_MultiResultSet() {
        using var multi = await MultiCmd.ExecuteMultiReaderAsync(cnn, out var cmd, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        cmd.Dispose();
        return p1.Sum() + p2.Sum();
    }

    [Benchmark, BenchmarkCategory("18. Multiple Result Sets Async")]
    public async Task<int> Rinku2_MultiResultSet() {
        using var multi = await cnn.ExecuteMultiReaderAsync(MultiSql, out var cmd, new { a = NextId(), b = NextId() });
        var p1 = await multi.QueryAsync<Post>();
        var p2 = await multi.QueryAsync<Post>();
        cmd.Dispose();
        return p1.Sum() + p2.Sum();
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

public record class Category(int Id, string? Name, string? Description) : IDbReadable;
