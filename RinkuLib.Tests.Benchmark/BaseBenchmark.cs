using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tests.TestContainers;
using Xunit.Sdk;

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

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new DBFixture<SqlConnection>();
        await _fixture.InitializeAsync();
        using var conn = _fixture.GetConnection();
        // Create Tables
        await conn.ExecuteAsync(@"
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
        await conn.ExecuteAsync("INSERT INTO Users (Id, Name, Email, Age) VALUES (1, 'User 1', 'user1@test.com', 30)");

        // Seed for QueryAll (100 rows)
        var users = Enumerable.Range(2, 100).Select(i => new { Id = i, Name = $"User {i}", Email = $"user{i}@test.com", Age = 20 + (i % 50) });
        await conn.ExecuteAsync("INSERT INTO Users (Id, Name, Email, Age) VALUES (@Id, @Name, @Email, @Age)", users);

        await conn.ExecuteAsync("INSERT INTO Categories (Id, Name, Description) VALUES (1, 'Electronics', 'Gadgets and stuff')");
        await conn.ExecuteAsync("INSERT INTO Products (Id, Name, CategoryId) VALUES (1, 'Laptop', 1)");



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

        // 3. QueryAll Async (Stream)
        var q3D = await Dapper_QueryAllAsync();
        var q3R = await Rinku_QueryAllAsync();
        if (q3D.Count != q3R.Count)
            throw new Exception("3. QueryAll Async (Stream): Sums differ.");
        for (var i = 0; i < q3D.Count; i++)
            if (q3D[i] != q3R[i])
                throw new Exception("3. QueryAll Async (Stream): Sums differ.");

        // 4. QueryAll Buffered Async
        var q4D = await Dapper_QueryAllBufferedAsync();
        var q4R = await Rinku_QueryAllBufferedAsync();
        if (q4D.Count != q4R.Count)
            throw new Exception("4. QueryAll Buffered Async: Collections differ.");
        for (var i = 0; i < q4D.Count; i++)
            if (q4D[i] != q4R[i])
                throw new Exception("4. QueryAll Buffered Async: Collections differ.");

        // 5. Dynamic Async
        var q5D = await Dapper_Dynamic();
        var q5R = await Rinku_DynaObject();
        if (q5D != q5R)
            throw new Exception("5. Dynamic Async: Values differ.");

        // 6. Complex Mapping
        var q6D = await Dapper_Complex();
        var q6R = await Rinku_Complex();
        if (q6D.Count != q6R.Count)
            throw new Exception("6. Complex Mapping: Results differ.");
        for (var i = 0; i < q6D.Count; i++)
            if (q6D[i] != q6R[i])
                throw new Exception("6. Complex Mapping: Results differ.");

        // 7. Execute Sync
        var q7D = Dapper_Execute();
        var q7R = Rinku_Execute();
        if (q7D != q7R)
            throw new Exception("7. Execute Sync: Row counts differ.");

        // 8. Execute Async
        var q8D = await Dapper_ExecuteAsync();
        var q8R = await Rinku_ExecuteAsync();
        if (q8D != q8R)
            throw new Exception("8. Execute Async: Row counts differ.");

        // 9. IN Clause
        var q9D = await Dapper_InClause();
        var q9R = await Rinku_InClause();
        if (q9D.Count != q9R.Count) 
            throw new Exception("9. IN Clause: Results differ.");
        for (var i = 0; i < q9D.Count; i++)
            if (q9D[i] != q9R[i])
                throw new Exception("9. IN Clause: Results differ.");

        Console.WriteLine("--- Validation Passed: All 9 Categories Match ---");
    }

    // --- QueryOne Sync ---
    [Benchmark(Baseline = true), BenchmarkCategory("1. QueryOne Sync")]
    public User? Dapper_QueryOne() => _fixture.GetConnection().QueryFirstOrDefault<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("1. QueryOne Sync")]
    public User? Rinku_QueryOne() => QueryUserCmd.QueryOne<User>(_fixture.GetConnection(), new { id = 1 });

    // --- QueryOne Async ---
    [Benchmark(Baseline = true), BenchmarkCategory("2. QueryOne Async")]
    public Task<User?> Dapper_QueryOneAsync() => _fixture.GetConnection().QueryFirstOrDefaultAsync<User>(SelectUserSql, new { id = 1 });

    [Benchmark, BenchmarkCategory("2. QueryOne Async")]
    public Task<User?> Rinku_QueryOneAsync() => QueryUserCmd.QueryOneAsync<User>(_fixture.GetConnection(), new { id = 1 });

    // --- QueryAll (Unbuffered/Streaming) ---
    [Benchmark(Baseline = true), BenchmarkCategory("3. QueryAll Async (Stream)")]
    public async Task<List<User>> Dapper_QueryAllAsync() {
        using var conn = _fixture.GetConnection();
        var items = conn.QueryUnbufferedAsync<User>(SelectAllUsersSql);
        var res = new List<User>();
        await foreach (var item in items)
            res.Add(item);
        return res;
    }

    [Benchmark, BenchmarkCategory("3. QueryAll Async (Stream)")]
    public async Task<List<User>> Rinku_QueryAllAsync() {
        using var conn = _fixture.GetConnection();
        var items = QueryAllUsersCmd.QueryAllAsync<User>(conn);
        var res = new List<User>();
        await foreach (var item in items)
            res.Add(item);
        return res;
    }

    // --- QueryAll Buffered ---
    [Benchmark(Baseline = true), BenchmarkCategory("4. QueryAll Buffered Async")]
    public async Task<List<User>> Dapper_QueryAllBufferedAsync() => (await _fixture.GetConnection().QueryAsync<User>(SelectAllUsersSql)).AsList();

    [Benchmark, BenchmarkCategory("4. QueryAll Buffered Async")]
    public Task<List<User>> Rinku_QueryAllBufferedAsync() => QueryAllUsersCmd.QueryAllBufferedAsync<User>(_fixture.GetConnection());

    // --- Dynamic / DynaObject ---
    [Benchmark(Baseline = true), BenchmarkCategory("5. Dynamic Async")]
    public async Task<string?> Dapper_Dynamic() {
        var row = await _fixture.GetConnection().QueryFirstOrDefaultAsync(SelectUserSql, new { id = 1 });
        return (string?)row?.Name;
    }

    [Benchmark, BenchmarkCategory("5. Dynamic Async")]
    public async Task<string?> Rinku_DynaObject() {
        var row = await QueryUserCmd.QueryOneAsync<DynaObject>(_fixture.GetConnection(), new { id = 1 });
        return row?.Get<string>("Name");
    }

    // --- Complex Mapping ---
    [Benchmark(Baseline = true), BenchmarkCategory("6. Complex Mapping")]
    public async Task<List<Product>> Dapper_Complex() => (await _fixture.GetConnection().QueryAsync<Product, Category, Product>(SelectComplexSql, (p, c) => { p.Category = c; return p; }, new { id = 1 })).AsList();

    [Benchmark, BenchmarkCategory("6. Complex Mapping")]
    public Task<List<Product>> Rinku_Complex() => QueryComplexCmd.QueryAllBufferedAsync<Product>(_fixture.GetConnection(), new { id = 1 });

    // --- Execute ---
    [Benchmark(Baseline = true), BenchmarkCategory("7. Execute Sync")]
    public int Dapper_Execute() => _fixture.GetConnection().Execute(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("7. Execute Sync")]
    public int Rinku_Execute() => ExecuteUpdateCmd.ExecuteQuery(_fixture.GetConnection(), new { name = "Test", id = 1 });

    // --- Execute Async ---
    [Benchmark(Baseline = true), BenchmarkCategory("8. Execute Async")]
    public Task<int> Dapper_ExecuteAsync() => _fixture.GetConnection().ExecuteAsync(UpdateSql, new { name = "Test", id = 1 });

    [Benchmark, BenchmarkCategory("8. Execute Async")]
    public Task<int> Rinku_ExecuteAsync() => ExecuteUpdateCmd.ExecuteQueryAsync(_fixture.GetConnection(), new { name = "Test", id = 1 });

    // --- Collection / IN Clause ---
    [Benchmark(Baseline = true), BenchmarkCategory("9. IN Clause")]
    public async Task<List<User>> Dapper_InClause() {
        var items = _fixture.GetConnection().QueryUnbufferedAsync<User>(InClauseSql, new { ids = Enumerable.Range(1, 5) });
        var res = new List<User>();
        await foreach (var item in items)
            res.Add(item);
        return res;
    }

    [Benchmark, BenchmarkCategory("9. IN Clause")]
    public async Task<List<User>> Rinku_InClause() {
        var items = InClauseCmd.QueryAllAsync<User>(_fixture.GetConnection(), new { ids = Enumerable.Range(1, 5) });
        var res = new List<User>();
        await foreach (var item in items)
            res.Add(item);
        return res;
    }

    [GlobalCleanup]
    public async ValueTask Cleanup() => await DisposeAsync();

    public async ValueTask DisposeAsync() {
        await _fixture.DisposeAsync();
    }
}

// --- MODELS ---

public record User(int Id, string Name, string Email, int Age) {
    public User(int Id, string Name) : this(Id, Name, "Default", 0) { }
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