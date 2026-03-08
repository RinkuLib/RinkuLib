using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;

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
    [Params(true, false)]
    public bool OpenCnn;

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new DBFixture<SqlConnection>();
        await _fixture.InitializeAsync();
        cnn = _fixture.GetConnection();
        using (cnn) {
            if (OpenCnn)
                await cnn.OpenAsync();
            // Create Tables
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

            // Seed Data
            await cnn.ExecuteAsync("INSERT INTO Users (Id, Name, Email, Age) VALUES (1, 'User 1', 'user1@test.com', 30)");

            // Seed for QueryAll (100 rows)
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

            // 1. QueryOne Sync
            var q1D = Dapper_QueryOne();
            var q1R = Rinku_QueryOne();
            if (q1D != q1R)
                throw new Exception("1. QueryOne Sync: Results differ.");
            
            // 2. QueryOne Async
            var q2D = await Dapper_QueryOneAsync();
            var q2R = await Rinku_QueryOneAsync();
            if (q2D != q2R)
                throw new Exception("2. QueryOne Async: Results differ.");

            // 3. QueryAll Sync (Stream)
            var q3D = Dapper_QueryAll();
            var q3R = Rinku_QueryAll();
            if (q3D != q3R)
                throw new Exception("3. QueryAll Sync (Stream): Sums differ.");

            // 4. QueryAll Buffered Sync
            var q4D = Dapper_QueryAllBuffered();
            var q4R = Rinku_QueryAllBuffered();
            if (q4D.Count != q4R.Count)
                throw new Exception("4. QueryAll Buffered Sync: Collections differ.");
            for (var i = 0; i < q4D.Count; i++)
                if (q4D[i] != q4R[i])
                    throw new Exception("4. QueryAll Buffered Sync: Collections differ.");

            // 5. QueryAll Async (Stream)
            var q5D = await Dapper_QueryAllAsync();
            var q5R = await Rinku_QueryAllAsync();
            if (q5D != q5R)
                throw new Exception("5. QueryAll Async (Stream): Sums differ.");

            // 6. QueryAll Buffered Async
            var q6D = await Dapper_QueryAllBufferedAsync();
            var q6R = await Rinku_QueryAllBufferedAsync();
            if (q6D.Count != q6R.Count)
                throw new Exception("6. QueryAll Buffered Async: Collections differ.");
            for (var i = 0; i < q6D.Count; i++)
                if (q6D[i] != q6R[i])
                    throw new Exception("6. QueryAll Buffered Async: Collections differ.");

            // 7. Dynamic Async
            var q7D = await Dapper_Dynamic();
            var q7R = await Rinku_DynaObject();
            if (q7D != q7R)
                throw new Exception("7. Dynamic Async: Values differ.");

            // 8. Complex Mapping
            var q8D = await Dapper_Complex();
            var q8R = await Rinku_Complex();
            if (q8D.Count != q8R.Count)
                throw new Exception("8. Complex Mapping: Results differ.");
            for (var i = 0; i < q8D.Count; i++)
                if (q8D[i] != q8R[i])
                    throw new Exception("8. Complex Mapping: Results differ.");

            // 9. Execute Sync
            var q9D = Dapper_Execute();
            var q9R = Rinku_Execute();
            if (q9D != q9R)
                throw new Exception("9. Execute Sync: Row counts differ.");

            // 10. Execute Async
            var q10D = await Dapper_ExecuteAsync();
            var q10R = await Rinku_ExecuteAsync();
            if (q10D != q10R)
                throw new Exception("10. Execute Async: Row counts differ.");

            // 11. IN Clause
            var q11D = await Dapper_InClause();
            var q11R = await Rinku_InClause();
            if (q11D != q11R)
                throw new Exception("11. IN Clause: Results differ.");

            Console.WriteLine("--- Validation Passed: All 11 Categories Match ---");
        }
        cnn = _fixture.GetConnection();
        if (OpenCnn)
            await cnn.OpenAsync();
    }


    [Benchmark(Baseline = true), BenchmarkCategory("1. QueryOne Sync")]
    public User? Dapper_QueryOne() => cnn.QueryFirstOrDefault<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("1. QueryOne Sync")]
    public User? Rinku_QueryOne() => QueryUserCmd.QueryOne<User>(cnn, new { id = 1 });
    

    [Benchmark(Baseline = true), BenchmarkCategory("2. QueryOne Async")]
    public Task<User?> Dapper_QueryOneAsync() => cnn.QueryFirstOrDefaultAsync<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("2. QueryOne Async")]
    public Task<User?> Rinku_QueryOneAsync() => QueryUserCmd.QueryOneAsync<User>(cnn, new { id = 1 });


    [Benchmark(Baseline = true), BenchmarkCategory("3. QueryAll Sync (Stream)")]
    public int Dapper_QueryAll() {
        var items = cnn.Query<User>(SelectAllUsersSql, buffered: false);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("3. QueryAll Sync (Stream)")]
    public int Rinku_QueryAll() {
        var items = QueryAllUsersCmd.QueryAll<User>(cnn);
        var sum = 0;
        foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("4. QueryAll Buffered Sync")]
    public List<User> Dapper_QueryAllBuffered() => cnn.Query<User>(SelectAllUsersSql).AsList();

    [Benchmark, BenchmarkCategory("4. QueryAll Buffered Sync")]
    public List<User> Rinku_QueryAllBuffered() => QueryAllUsersCmd.QueryAllBuffered<User>(cnn);


    [Benchmark(Baseline = true), BenchmarkCategory("5. QueryAll Async (Stream)")]
    public async Task<int> Dapper_QueryAllAsync() {
        var items = cnn.QueryUnbufferedAsync<User>(SelectAllUsersSql);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("5. QueryAll Async (Stream)")]
    public async Task<int> Rinku_QueryAllAsync() {
        var items = QueryAllUsersCmd.QueryAllAsync<User>(cnn);
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }


    [Benchmark(Baseline = true), BenchmarkCategory("6. QueryAll Buffered Async")]
    public async Task<List<User>> Dapper_QueryAllBufferedAsync() => (await cnn.QueryAsync<User>(SelectAllUsersSql)).AsList();

    [Benchmark, BenchmarkCategory("6. QueryAll Buffered Async")]
    public Task<List<User>> Rinku_QueryAllBufferedAsync() => QueryAllUsersCmd.QueryAllBufferedAsync<User>(cnn);


    [Benchmark(Baseline = true), BenchmarkCategory("7. Dynamic Async")]
    public async Task<(int, string?, string?, int)> Dapper_Dynamic() {
        var row = await cnn.QueryFirstOrDefaultAsync(SelectUserSql, new { id = 1 });
        if (row is null)
            return default;
        return ((int)row.Id, (string?)row.Name, (string?)row.Email, (int)row.Age);
    }

    [Benchmark, BenchmarkCategory("7. Dynamic Async")]
    public async Task<(int, string?, string?, int)> Rinku_DynaObject() {
        var row = await QueryUserCmd.QueryOneAsync<DynaObject>(cnn, new { id = 1 });
        if (row is null)
            return default;
        return (row.Get<int>("Id"), row.Get<string>("Name"), row.Get<string>("Email"), row.Get<int>("Age"));
    }

    // --- Complex Mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("8. Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await cnn.QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("8. Complex Mapping")]
    public Task<List<Product>> Rinku_Complex() => QueryComplexCmd.QueryAllBufferedAsync<Product>(cnn, new { id = 1 });

    // --- Execute ---
    [Benchmark(Baseline = true), BenchmarkCategory("9. Execute Sync")]
    public int Dapper_Execute() => cnn.Execute(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("9. Execute Sync")]
    public int Rinku_Execute() => ExecuteUpdateCmd.Execute(cnn, new { name = "Test", id = 1 });

    // --- Execute Async ---
    [Benchmark(Baseline = true), BenchmarkCategory("10. Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => cnn.ExecuteAsync(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("10. Execute Async")]
    public Task<int> Rinku_ExecuteAsync() => ExecuteUpdateCmd.ExecuteAsync(cnn, new { name = "Test", id = 1 });

    // --- Collection / IN Clause ---
    [Benchmark(Baseline = true), BenchmarkCategory("11. IN Clause")]
    public async Task<int> Dapper_InClause() {
        var items = cnn.QueryUnbufferedAsync<User>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var sum = 0;
        await foreach (var item in items)
            sum += item.Sum();
        return sum;
    }

    [Benchmark, BenchmarkCategory("11. IN Clause")]
    public async Task<int> Rinku_InClause() {
        var items = InClauseCmd.QueryAllAsync<User>(cnn, new { ids = Enumerable.Range(1, 5) });
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

// --- MODELS ---

public record User(int Id, string Name, string Email, int Age) {
    public User(int Id, string Name) : this(Id, Name, "Default", 0) { }
    public int Sum() => Id + Name.Length + Email.Length + Age;
}

public class Product {
    public int Id { get; set; }
    public string? Name { get; set; }
    public Category? Category { get; set; }
    public static bool operator ==(Product? p1, Product? p2) {
        // Handle nulls on either side
        if (ReferenceEquals(p1, p2))
            return true;
        if (p1 is null || p2 is null)
            return false;

        // Compare properties
        return p1.Id == p2.Id &&
               p1.Name == p2.Name &&
               p1.Category == p2.Category;
    }

    public static bool operator !=(Product? p1, Product? p2) => !(p1 == p2);

    public override bool Equals(object? obj) => obj is Product other && this == other;

    public override int GetHashCode() => HashCode.Combine(Id, Name, Category);
}

public record class Category(int Id, string? Name, string? Description) : IDbReadable;