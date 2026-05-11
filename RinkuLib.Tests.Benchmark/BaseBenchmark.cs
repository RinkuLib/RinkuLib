using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Tests.Benchmark;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
//[ShortRunJob]
public class BaseBenchmark : IAsyncDisposable {
    private DBFixture<SqlConnection> _fixture = null!;

    // --- SQL & Blueprints ---
    private const string SelectUserSql = "SELECT Id, Name, Email, Age FROM Users WHERE Id = @id";
    private const string SelectAllUsersSql = "SELECT Id, Name, Email, Age FROM Users";
    private const string SelectComplexSql = "SELECT p.Id, p.Name, c.Id, c.Name, c.Description FROM Products p INNER JOIN Categories c ON p.CategoryId = c.Id WHERE p.Id = @id";
    private const string UpdateSql = "UPDATE Users SET Name = @name WHERE Id = @id";
    private const string InClauseSql = "SELECT Id, Name FROM Users WHERE Id IN @ids";

    private static readonly QueryCommand QueryUserCmd = new(SelectUserSql);
    private static readonly QueryCommand QueryAllUsersCmd = new(SelectAllUsersSql);
    private static readonly QueryCommand QueryComplexCmd = new("SELECT p.Id, p.Name, c.Id AS CategoryId, c.Name AS CategoryName, c.Description AS CategoryDescription FROM Products p INNER JOIN Categories c ON p.CategoryId = c.Id WHERE p.Id = @id");
    private static readonly QueryCommand ExecuteUpdateCmd = new(UpdateSql);
    private static readonly QueryCommand InClauseCmd = new("SELECT Id, Name FROM Users WHERE Id IN (@ids_X)");

    private SqlConnection cnn = null!;
    //[Params(true, false)]
    public bool OpenCnn = false;

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new DBFixture<SqlConnection>();
        await _fixture.InitializeAsync();
        cnn = _fixture.GetConnection();
        using (cnn) {
            if (OpenCnn)
                await cnn.OpenAsync();

            await cnn.ExecuteAsync(@"
        CREATE TABLE Users (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100),
            Email NVARCHAR(100),
            Age INT
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

            await cnn.ExecuteAsync("INSERT INTO Users (Id, Name, Email, Age) VALUES (1, 'User 1', 'user1@test.com', 30)");

            var users = Enumerable.Range(2, 100).Select(i => new { Id = i, Name = $"User {i}", Email = $"user{i}@test.com", Age = 20 + (i % 50) });
            await cnn.ExecuteAsync("INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)", users);

            await cnn.ExecuteAsync("INSERT INTO Categories (Id, Name, Description) VALUES (1, 'Electronics', 'Gadgets and stuff')");
            await cnn.ExecuteAsync("INSERT INTO Products (Id, Name, CategoryId) VALUES (1, 'Laptop', 1)");

        }
        cnn = _fixture.GetConnection();
        using (cnn) {
            if (OpenCnn)
                await cnn.OpenAsync();

            Console.WriteLine("--- Starting Full Equivalence Validation ---");
            await Validate();
            Console.WriteLine("--- Validation Passed: All 15 Categories Match ---");

            Console.WriteLine("--- Starting Full Equivalence Validation (second pass) ---");
            await Validate();
            Console.WriteLine("--- Validation Passed: All 15 Categories Match (second pass) ---");
        }
        cnn = _fixture.GetConnection();
        if (OpenCnn)
            await cnn.OpenAsync();
    }
    private async Task Validate() {
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
    }

    [Benchmark(Baseline = true), BenchmarkCategory("1. Query one Sync")]
    public User Dapper_QueryFirst() => cnn.QueryFirst<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("1. Query one Sync")]
    public User Rinku_QueryT() => QueryUserCmd.Query<User>(cnn, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("2. Query one (or default) Sync")]
    public User? Dapper_QueryFirstOrDefault() => cnn.QueryFirstOrDefault<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("2. Query one (or default) Sync")]
    public User? Rinku_QueryOptionalT() => QueryUserCmd.Query<Optional<User>>(cnn, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("3. Query one (single) Sync")]
    public User Dapper_QuerySingle() => cnn.QuerySingle<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("3. Query one (single) Sync")]
    public User Rinku_QuerySingleT() => QueryUserCmd.Query<Single<User>>(cnn, new { id = 1 });


    [Benchmark(Baseline = true), BenchmarkCategory("4. Query one Async")]
    public async Task<User> Dapper_QueryFirstAsync() => await cnn.QueryFirstAsync<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("4. Query one Async")]
    public async Task<User> Rinku_QueryTAsync() => await QueryUserCmd.QueryAsync<User>(cnn, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("5. Query one (or default) Async")]
    public async Task<User?> Dapper_QueryFirstOrDefaultAsync() => await cnn.QueryFirstOrDefaultAsync<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("5. Query one (or default) Async")]
    public async Task<User?> Rinku_QueryOptionalTAsync() => await QueryUserCmd.QueryAsync<Optional<User>>(cnn, new { id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("6. Query one (single) Async")]
    public async Task<User> Dapper_QuerySingleAsync() => await cnn.QuerySingleAsync<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("6. Query one (single) Async")]
    public async Task<User> Rinku_QuerySingleTAsync() => await QueryUserCmd.QueryAsync<Single<User>>(cnn, new { id = 1 });


    [Benchmark(Baseline = true), BenchmarkCategory("7. Query Sync (Stream)")]
    public int Dapper_QueryUnbuffered() {
        var items = cnn.Query<User>(SelectAllUsersSql, buffered: false);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("7. Query Sync (Stream)")]
    public int Rinku_QueryIEnumerable() {
        var items = QueryAllUsersCmd.Query<IEnumerable<User>>(cnn)!;
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("8. Query Buffered Sync")]
    public List<User> Dapper_QueryBuffered() => cnn.Query<User>(SelectAllUsersSql).AsList();

    [Benchmark, BenchmarkCategory("8. Query Buffered Sync")]
    public List<User> Rinku_QueryList() => QueryAllUsersCmd.Query<List<User>>(cnn)!;


    [Benchmark(Baseline = true), BenchmarkCategory("9. Query Async (Stream)")]
    public async Task<int> Dapper_QueryUnbufferedAsync() {
        var items = cnn.QueryUnbufferedAsync<User>(SelectAllUsersSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("9. Query Async (Stream)")]
    public async Task<int> Rinku_StreamQueryAsync() {
        var items = QueryAllUsersCmd.StreamQueryAsync<User>(cnn);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("10. Query Buffered Async")]
    public async Task<List<User>> Dapper_QueryAsyncBuffered() => (await cnn.QueryAsync<User>(SelectAllUsersSql)).AsList();

    [Benchmark, BenchmarkCategory("10. Query Buffered Async")]
    public Task<List<User>> Rinku_QueryAsyncList() => QueryAllUsersCmd.QueryAsync<List<User>>(cnn)!;


    [Benchmark(Baseline = true), BenchmarkCategory("11. Dynamic Async")]
    public async Task<(int, string?, string?, int)> Dapper_QueryAsyncDynamic() {
        var row = await cnn.QueryFirstAsync(SelectUserSql, new { id = 1 });
        return ((int)row.Id, (string?)row.Name, (string?)row.Email, (int)row.Age);
    }

    [Benchmark, BenchmarkCategory("11. Dynamic Async")]
    public async Task<(int, string?, string?, int)> Rinku_QueryAsyncDynaObject() {
        var row = await QueryUserCmd.QueryAsync<DynaObject>(cnn, new { id = 1 });
        return (row.Get<int>("Id"), row.Get<string>("Name"), row.Get<string>("Email"), row.Get<int>("Age"));
    }

    [Benchmark(Baseline = true), BenchmarkCategory("12. Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await cnn.QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("12. Complex Mapping")]
    public Task<List<Product>> Rinku_Complex() => QueryComplexCmd.QueryAsync<List<Product>>(cnn, new { id = 1 })!;

    [Benchmark(Baseline = true), BenchmarkCategory("13. Execute Sync")]
    public int Dapper_Execute() => cnn.Execute(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("13. Execute Sync")]
    public int Rinku_Execute() => ExecuteUpdateCmd.Execute(cnn, new { name = "Test", id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("14. Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("14. Execute Async")]
    public Task<int> Rinku_ExecuteAsync() => ExecuteUpdateCmd.ExecuteAsync(cnn, new { name = "Test", id = 1 });

    [Benchmark(Baseline = true), BenchmarkCategory("15. IN Clause")]
    public async Task<int> Dapper_InClause() {
        var items = cnn.QueryUnbufferedAsync<User>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("15. IN Clause")]
    public async Task<int> Rinku_InClause() {
        var items = InClauseCmd.StreamQueryAsync<User>(cnn, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }
    
    [GlobalCleanup]
    public async ValueTask Cleanup() => await DisposeAsync();

    public async ValueTask DisposeAsync() {
        await cnn.DisposeAsync();
        await _fixture.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}


public record User(int Id, string Name, string Email, int Age) {
    public User(int Id, string Name) : this(Id, Name, "Default", 0) { }
    public int Sum() => Id + Name.Length + Email.Length + Age;
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