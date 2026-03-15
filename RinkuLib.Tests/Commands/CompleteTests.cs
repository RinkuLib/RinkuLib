using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using RinkuLib.Commands;
using RinkuLib.DBActions;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Commands; 
public class CompleteTests {
    private readonly ITestOutputHelper _output;
    public CompleteTests(ITestOutputHelper output) {
        _output = output;
#if DEBUG
        //Generator.Write = output.WriteLine;
#endif
        SQLitePCL.Batteries.Init();
    }
    public static DbConnection GetDbCnn() {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDB.db");
        return new SqliteConnection($"Data Source={dbPath}");
    }
    [Fact]
    public async Task TestBasicStringUsageQueryOneAsync_Null_Prevented() {
        QueryCommand BasicStringUsageNULL = new("select 'value' as [Value] union all select CAST(NULL AS TEXT) union all select @txt");
        using var cnn = GetDbCnn();

        var res = BasicStringUsageNULL.QueryAllAsync<NotNull<string>>(cnn, new { txt = "def" }, ct: TestContext.Current.CancellationToken);

        await using var enumerator = res.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("value", (string)enumerator.Current);

        await Assert.ThrowsAsync<NullValueAssignmentException>(async () => {
            await enumerator.MoveNextAsync();
        });
    }

    [Fact]
    public void Example1_StaticQuery() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = builder.QueryOne<Person>(cnn);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);

        var builder2 = query.StartBuilder([("@Active", 0)]);
        var p2 = builder2.QueryOne<Person>(cnn);
        Assert.NotNull(p2);
        Assert.Equal(2, p2.ID);
        Assert.Equal("Victor", p2.Username);
        Assert.Equal("abc@email.com", p2.Email);
    }
    [Fact]
    public void Double_to_Decimal() {
        var query = new QueryCommand("SELECT Number FROM Users WHERE ID = 1");
        using var cnn = GetDbCnn();
        var d = query.QueryOne<decimal>(cnn);
        Assert.Equal(10.2M, d);
    }
    [Fact]
    public void Example1_StaticQuery_Reuse() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        using var cmd = cnn.CreateCommand();
        var builder = query.StartBuilder(cmd);
        builder.Use("@Active", true);
        var p = builder.QueryOne<Person>();
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);

        builder.Use("@Active", 0);
        var p2 = builder.QueryOne<Person>();
        Assert.NotNull(p2);
        Assert.Equal(2, p2.ID);
        Assert.Equal("Victor", p2.Username);
        Assert.Equal("abc@email.com", p2.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Async() {
        var query = new QueryCommand("SELECT ID, Name, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var p = await builder.QueryOneAsync<Person>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(p);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(p.Email);
    }
    [Fact]
    public async Task Example1_StaticQuery_Object() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        builder.Use("@Active", true);
        using var cnn = GetDbCnn();
        var (p, email) = builder.QueryOne<(Person, object)>(cnn);
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_Obj() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object)>(cnn, new PersonParam(true));
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_T() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object), PersonParam>(cnn, new PersonParam(true));
        Assert.Equal(1, p.ID);
        Assert.Equal("John", p.Username);
        Assert.Null(email);
    }
    [Fact]
    public async Task Use_Complete_T_False() {
        var query = new QueryCommand("SELECT ID, Name, Email AS Emaill FROM Users WHERE IsActive = @Active");
        using var cnn = GetDbCnn();
        var (p, email) = query.QueryOne<(Person, object), PersonParam>(cnn, new PersonParam(false));
        Assert.Equal(2, p.ID);
        Assert.Equal("Victor", p.Username);
        Assert.Equal("abc@email.com", email);
    }
    [Fact]
    public void Using_Two_DifferentAnonymous() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        using var cnn = GetDbCnn();
        nint handle1 = default;
        using (var cmd1 = cnn.CreateCommand()) {
            var val1 = new { ID = 1 };
            handle1 = val1.GetType().TypeHandle.Value;
            Span<bool> usageMap1 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd1, val1, usageMap1);
            Assert.Equal("SELECT * FROM u WHERE u.ID = @ID", cmd1.CommandText);
            Assert.True(usageMap1[0]);
            Assert.False(usageMap1[1]);
        }
        using var cmd2 = cnn.CreateCommand();
        var val2 = new { OtherID = 1 };
        Assert.NotEqual(handle1, val2.GetType().TypeHandle.Value);
        Span<bool> usageMap2 = stackalloc bool[query.Mapper.Count];
        query.SetCommand(cmd2, val2, usageMap2);
        Assert.Equal("SELECT * FROM u WHERE u.OtherID = @OtherID", cmd2.CommandText);
        Assert.False(usageMap2[0]);
        Assert.True(usageMap2[1]);
    }
    [Fact]
    public void Using_Two_DifferentTyped() {
        var query = new QueryCommand("SELECT * FROM u WHERE u.ID = ?@ID AND u.OtherID = ?@OtherID");
        using var cnn = GetDbCnn();
        using (var cmd1 = cnn.CreateCommand()) {
            Span<bool> usageMap1 = stackalloc bool[query.Mapper.Count];
            query.SetCommand(cmd1, new A1 { ID = 1 }, usageMap1);
            Assert.Equal("SELECT * FROM u WHERE u.ID = @ID", cmd1.CommandText);
            Assert.True(usageMap1[0]);
            Assert.False(usageMap1[1]);
        }
        using var cmd2 = cnn.CreateCommand();
        Span<bool> usageMap2 = stackalloc bool[query.Mapper.Count];
        query.SetCommand(cmd2, new A2 { OtherID = 1 }, usageMap2);
        Assert.Equal("SELECT * FROM u WHERE u.OtherID = @OtherID", cmd2.CommandText);
        Assert.False(usageMap2[0]);
        Assert.True(usageMap2[1]);
    }
    [Fact]
    public void Cache_Bug() {
        var template = "SELECT ID, /*Name*/Name FROM Users WHERE Name = ?@Name";
        var query = new QueryCommand(template);

        using var cnn = GetDbCnn();
        var builder1 = query.StartBuilder();
        var dObj1 = builder1.QueryOne<DynaObject>(cnn);
        Assert.NotNull(dObj1);
        Assert.Single(dObj1);

        var builder2 = query.StartBuilder();
        builder2.Use("Name");
        builder2.Use("@Name", "Victor");
        var dObj2 = builder2.QueryOne<DynaObject>(cnn);
        Assert.NotNull(dObj2);
        Assert.Equal(2, dObj2.Count);

        var builder3 = query.StartBuilder();
        builder3.Use("@Name", "Victor");
        var dObj3 = builder3.QueryOne<DynaObject>(cnn);
        Assert.NotNull(dObj3);
        Assert.Single(dObj3);

        var builder4 = query.StartBuilder();
        builder4.Use("Name");
        var dObj4 = builder4.QueryOne<DynaObject>(cnn);
        Assert.NotNull(dObj4);
        Assert.Equal(2, dObj4.Count);
    }
    [Fact]
    public async Task ToManyRelation_One_Level() {
        var query = new QueryCommand("/*!.ToMany*/SELECT ID, Label /*.Products*/SELECT c.ID, p.ID, p.Name FROM Categories c /*.Products*/INNER JOIN Products p ON c.ID = p.CategoryID");
        static int GetID(ref Category c) => c.ID;
        static void SetCol(ref Category c, PooledArray<Product> arr) => c.Products = arr.ToList();
        var productInit = ToManyRelation<Category, Product>.New(GetID, SetCol);
        using var cnn = GetDbCnn();
        var cats = await query.QueryAllBufferedAsync<Category>(cnn, ct: TestContext.Current.CancellationToken);
        query.InitWithDB(cnn, cats, productInit, RequestProducts.Instance);
        foreach (var c in cats) {
            Assert.NotEmpty(c.Products);
        }
    }
    [Fact]
    public async Task ToManyRelation_Two_Level() {
        var query = new QueryCommand("/*!.ToMany*/SELECT ID, Label /*.Products*/SELECT c.ID, p.ID, p.Name /*.Products.OwnedBy*/SELECT c.ID, p.ID, u.ID, u.Name, u.Email FROM Categories c /*.Products|.Products.OwnedBy*/INNER JOIN Products p ON c.ID = p.CategoryID /*.Products.OwnedBy*/INNER JOIN UserProducts up ON up.ProductID = p.ID /*.Products.OwnedBy*/INNER JOIN Users u ON u.ID = up.UserID");
        static int GetID(ref Category c) => c.ID;
        static void SetCol(ref Category c, PooledArray<Product> arr) => c.Products = arr.ToList();
        static ListAccess<Product> GetCol(ref Category c) => c.Products;
        static int GetID2(ref Product p) => p.ID;
        static void SetCol2(ref Product p, PooledArray<User> arr) => p.OwnedBy = arr.ToList();
        var productInit = ToManyRelation<Category, Product>.New(GetID, SetCol);
        var productOwnedByInit = ToManyRelation<Category, User>.New(GetID, GetCol, GetID2, SetCol2);

        using var cnn = GetDbCnn();
        var cats = await query.QueryAllBufferedAsync<Category>(cnn, ct: TestContext.Current.CancellationToken);
        query.InitWithDB(cnn, cats, productInit, RequestProducts.Instance);
        query.InitWithDB(cnn, cats, productOwnedByInit, RequestUsers.Instance);
        int[] counts = [5, 4, 2];
        var ind = 0;
        Assert.Equal(counts.Length, cats.Count);
        foreach (var c in cats) {
            Assert.Equal(counts[ind++], c.Products.Count);
            foreach (var prod in c.Products) {
                if (prod.ID != 1)
                    continue;
                Assert.Equal(2, prod.OwnedBy.Count);
            }
        }
    }
    [Fact]
    public async Task ToManyRelation_Two_Level_Struct_And_Array() {
        var query = new QueryCommand("/*!.ToMany*/SELECT ID, Label /*.Products*/SELECT c.ID, p.ID, p.Name /*.Products.OwnedBy*/SELECT c.ID, p.ID, u.ID, u.Name, u.Email FROM Categories c /*.Products|.Products.OwnedBy*/INNER JOIN Products p ON c.ID = p.CategoryID /*.Products.OwnedBy*/INNER JOIN UserProducts up ON up.ProductID = p.ID /*.Products.OwnedBy*/INNER JOIN Users u ON u.ID = up.UserID");
        static int GetID(ref CategoryStruct c) => c.ID;
        static void SetCol(ref CategoryStruct c, PooledArray<ProductStruct> arr) => c.ProductStructsArray = arr.ToArray();
        static ArrayAccess<ProductStruct> GetCol(ref CategoryStruct c) => c.ProductStructsArray;
        static int GetID2(ref ProductStruct p) => p.ID;
        static void SetCol2(ref ProductStruct p, PooledArray<UserStruct> arr) => p.OwnedByStructArray = arr.ToArray();
        var productInit = ToManyRelation<CategoryStruct, ProductStruct>.New(GetID, SetCol);
        var productOwnedByInit = ToManyRelation<CategoryStruct, UserStruct>.New(GetID, GetCol, GetID2, SetCol2);

        using var cnn = GetDbCnn();
        var cats = await query.QueryAllBufferedAsync<CategoryStruct>(cnn, ct: TestContext.Current.CancellationToken);
        query.InitWithDB(cnn, cats, productInit, RequestProducts.Instance);
        query.InitWithDB(cnn, cats, productOwnedByInit, RequestUsers.Instance);
        int[] counts = [5, 4, 2];
        var ind = 0;
        Assert.Equal(counts.Length, cats.Count);
        foreach (var c in cats) {
            Assert.Equal(counts[ind++], c.ProductStructsArray.Length);
            foreach (var prod in c.ProductStructsArray) {
                if (prod.ID != 1)
                    continue;
                Assert.Equal(2, prod.OwnedByStructArray.Length);
            }
        }
    }
    [Fact]
    public async Task ToManyRelation_One_Level_OneItem() {
        var query = new QueryCommand("/*!.ToMany*/SELECT ID, Label /*.Products*/SELECT c.ID, p.ID, p.Name FROM Categories c /*.Products*/INNER JOIN Products p ON c.ID = p.CategoryID WHERE c.ID = ?@CatID");
        static int GetID(ref Category c) => c.ID;
        static void SetCol(ref Category c, PooledArray<Product> arr) => c.Products = arr.ToList();
        var productInit = ToManyRelation<Category, Product>.New(GetID, SetCol);
        using var cnn = GetDbCnn();
        var c = await query.QueryOneAsync<Category>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(c);
        query.InitWithDB(cnn, ref c, productInit, new RequestProducts(1));
        Assert.NotEmpty(c.Products);
    }
    [Fact]
    public async Task ToManyRelation_Two_Level_OneItem() {
        var query = new QueryCommand("/*!.ToMany*/SELECT ID, Label /*.Products*/SELECT c.ID, p.ID, p.Name /*.Products.OwnedBy*/SELECT c.ID, p.ID, u.ID, u.Name, u.Email FROM Categories c /*.Products|.Products.OwnedBy*/INNER JOIN Products p ON c.ID = p.CategoryID /*.Products.OwnedBy*/INNER JOIN UserProducts up ON up.ProductID = p.ID /*.Products.OwnedBy*/INNER JOIN Users u ON u.ID = up.UserID WHERE c.ID = ?@CatID");

        static int GetID(ref Category c) => c.ID;
        static void SetCol(ref Category c, PooledArray<Product> arr) => c.Products = arr.ToList();
        static ListAccess<Product> GetCol(ref Category c) => c.Products;
        static int GetID2(ref Product p) => p.ID;
        static void SetCol2(ref Product p, PooledArray<User> arr) => p.OwnedBy = arr.ToList();
        var productInit = ToManyRelation<Category, Product>.New(GetID, SetCol);
        var productOwnedByInit = ToManyRelation<Category, User>.New(GetID, GetCol, GetID2, SetCol2);

        using var cnn = GetDbCnn();
        var c = await query.QueryOneAsync<Category>(cnn, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(c);
        query.InitWithDB(cnn, ref c, productInit, new RequestProducts(1));
        query.InitWithDB(cnn, ref c, productOwnedByInit, new RequestUsers(1));
        int[] counts = [5, 4, 2];
        var ind = 0;
        Assert.Equal(counts[ind++], c.Products.Count);
        foreach (var prod in c.Products) {
            if (prod.ID != 1)
                continue;
            Assert.Equal(2, prod.OwnedBy.Count);
        }
    }
    [Fact]
    public async Task Using_Default_Nullable() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE ID = @ID");
        using var cnn = GetDbCnn();
        var withNullable = query.QueryOne<WithDefNullable>(cnn, new { ID = 1 });
        Assert.NotNull(withNullable);
        Assert.Equal(1, withNullable.ID);
        Assert.Equal("John", withNullable.Name);
        Assert.Null(withNullable.NotUsed);
    }
}
public record struct PersonParam(bool Active);
public record Person(int ID, [Alt("Name")]string Username, string? Email) : IDbReadable {
    public Person(int ID, [Alt("Name")]string Username) :this(ID, Username, null) { }
}
public sealed class A1 {
    public int ID;
}
public sealed class A2 {
    public int OtherID;
}
public record class Category(int ID, [Alt("Label")] string Name) : IDbReadable {
    public List<Product> Products = [];
    public Product[] ProductsArray = [];
    public List<ProductStruct> ProductStructs = [];
    public ProductStruct[] ProductStructsArray = [];
}
public record struct CategoryStruct(int ID, [Alt("Label")] string Name) {
    public List<Product> Products = [];
    public Product[] ProductsArray = [];
    public List<ProductStruct> ProductStructs = [];
    public ProductStruct[] ProductStructsArray = [];
}
public record class Product(int ID, string Name, Category? Category = null) : IDbReadable {
    public User[] OwnedByArray = [];
    public List<User> OwnedBy = [];
    public UserStruct[] OwnedByStructArray = [];
    public List<UserStruct> OwnedByStruct = [];
}
public record struct ProductStruct(int ID, string Name, CategoryStruct? Category = null) {
    public User[] OwnedByArray = [];
    public List<User> OwnedBy = [];
    public UserStruct[] OwnedByStructArray = [];
    public List<UserStruct> OwnedByStruct = [];
}
public record User(int ID, [Alt("Name")] string Username, string? Email = null);
public record struct UserStruct(int ID, [Alt("Name")] string Username, string? Email = null);
public record class WithDefNullable(int ID, string Name, int? NotUsed = null);
[UsesBoolConds(".ToMany", ".Products")]
public record class RequestProducts([property: NotDefault]int CatID) {
    public static readonly RequestProducts Instance = new(0);
}
[UsesBoolConds(".ToMany", ".Products.OwnedBy")]
public record class RequestUsers([property: NotDefault] int CatID) {
    public static readonly RequestUsers Instance = new(0);
}
/// <summary></summary>
public static class ItemDBInit {
    /// <summary></summary>
    public static void InitWithDB<TParent>(this QueryCommand command, DbConnection cnn, IEnumerable<TParent> parentsToInit, DbAction<TParent> action, object? parameterObj = null) {
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            if (wasClosed)
                cnn.Open();
            using var cmd = cnn.CreateCommand();
            QueryCommandUsingObjectParam getter = new(command, cmd, parameterObj);
            action.Handle(parentsToInit, getter);
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <summary></summary>
    public static void InitWithDB<TParent>(this QueryCommand command, DbConnection cnn, List<TParent> parentsToInit, DbAction<TParent> action, object? parameterObj = null) {
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            if (wasClosed)
                cnn.Open();
            using var cmd = cnn.CreateCommand();
            QueryCommandUsingObjectParam getter = new(command, cmd, parameterObj);
            action.Handle(new ListAccess<TParent>(parentsToInit), getter);
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <summary></summary>
    public static void InitWithDB<TParent>(this QueryCommand command, DbConnection cnn, ref TParent parentToInit, DbAction<TParent> action, object? parameterObj = null) {
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            if (wasClosed)
                cnn.Open();
            using var cmd = cnn.CreateCommand();
            QueryCommandUsingObjectParam getter = new(command, cmd, parameterObj);
            action.Handle(ref parentToInit, getter);
        }
        finally {
            if (wasClosed)
                cnn.Close();
        }
    }
    /// <summary></summary>
    public static async ValueTask InitWithDBAsync<TParent>(this QueryCommand command, DbConnection cnn, TParent parentToInit, DbAction<TParent> action, object? parameterObj = null, CancellationToken ct = default) {
        var wasClosed = cnn.State != ConnectionState.Open;
        try {
            if (wasClosed)
                await cnn.OpenAsync(ct).ConfigureAwait(false);
            using var cmd = cnn.CreateCommand();
            QueryCommandUsingObjectParam getter = new(command, cmd, parameterObj);
            await action.HandleAsync(parentToInit, getter, ct).ConfigureAwait(false);
        }
        finally {
            if (wasClosed)
                await cnn.CloseAsync().ConfigureAwait(false);
        }
    }
}