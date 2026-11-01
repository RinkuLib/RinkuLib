using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Compilation;

public class CommandHookCompilationTests
{
    [Fact]
    public async Task Unimplemented_hooks_compile_with_nullable_input_and_output_parameters()
    {
        using var directory = new TempDirectory();
        var query = new QuerySetting { MethodName = "Refresh", Target = "SELECT 1", SourceType = QuerySourceType.Text, GenerateHooks = true };
        var (_, code, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, Schema(query.Target));
        Assembly assembly = GeneratedAssemblyCompiler.Compile(code, support);
        MethodInfo method = assembly.GetType("TestApp.Generated.DbCommands")!.GetMethod(query.MethodName)!;
        Assert.Empty(method.GetMethodBody()!.ExceptionHandlingClauses);
        Assert.DoesNotContain(assembly.GetType("TestApp.Generated.DbCommands")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic), candidate => candidate.Name is "BeforeRefresh" or "AfterRefresh");
        using var connection = new SqlConnection();
        object?[] arguments = [connection, null, null, (long?)42, null, null];
        using var command = (DbCommand)method.Invoke(null, arguments)!;
        Assert.Equal(DBNull.Value, command.Parameters[0].Value);
        Assert.Equal(DBNull.Value, command.Parameters[1].Value);
        Assert.Equal(42L, command.Parameters[2].Value);
        Assert.Same(command.Parameters[2], arguments[4]);
        Assert.Same(command.Parameters[3], arguments[5]);
        Assert.Equal(ParameterDirection.InputOutput, ((DbParameter)arguments[4]!).Direction);
        Assert.Equal(ParameterDirection.Output, ((DbParameter)arguments[5]!).Direction);
        Assert.DoesNotContain("System.Reflection", code + support);
        Assert.DoesNotContain("MethodInfo", code + support);
    }

    [Fact]
    public async Task External_partials_run_both_before_hooks_in_order_and_receive_the_completed_command()
    {
        using var directory = new TempDirectory();
        var query = new QuerySetting { MethodName = "Refresh", Target = "SELECT 1", SourceType = QuerySourceType.Text, GenerateHooks = true };
        var (_, code, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, Schema(query.Target));
        Assembly assembly = GeneratedAssemblyCompiler.Compile(code, support, """
using System.Data.Common;
namespace TestApp.Generated;
public static partial class DbCommands
{
    public static DbConnection BeforeConnection = null!;
    public static int Stage;
    public static DbCommand Completed = null!;
    static partial void BeforeRefresh(DbConnection connection)
    {
        BeforeConnection = connection;
        Stage = 1;
    }
    static partial void BeforeRefresh(DbConnection connection, ref string? name, ref byte[]? bytes, ref long? value)
    {
        if (Stage != 1)
        {
            throw new System.InvalidOperationException();
        }
        Stage = 2;
        name = "Changed";
        bytes = [1, 2];
        value = 9;
    }
    static partial void AfterRefresh(DbCommand command)
    {
        if (Stage != 2 || command.Parameters.Count != 4 || command.CommandText != "SELECT 1")
        {
            throw new System.InvalidOperationException();
        }
        Stage = 3;
        command.CommandTimeout = 7;
        Completed = command;
    }
}
""");
        Type factory = assembly.GetType("TestApp.Generated.DbCommands")!;
        using var connection = new SqlConnection();
        object?[] arguments = [connection, null, null, null, null, null];
        using var command = (DbCommand)factory.GetMethod(query.MethodName)!.Invoke(null, arguments)!;
        Assert.Same(connection, command.Connection);
        Assert.Same(connection, factory.GetField("BeforeConnection")!.GetValue(null));
        Assert.Equal(3, factory.GetField("Stage")!.GetValue(null));
        Assert.Equal("Changed", command.Parameters[0].Value);
        Assert.Equal(new byte[] { 1, 2 }, command.Parameters[1].Value);
        Assert.Equal(9L, command.Parameters[2].Value);
        Assert.Equal(7, command.CommandTimeout);
        Assert.Same(command, factory.GetField("Completed")!.GetValue(null));
        Assert.Same(command.Parameters[2], arguments[4]);
        Assert.Same(command.Parameters[3], arguments[5]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hooks_can_reject_a_call_and_the_after_implementation_owns_disposal(bool before)
    {
        using var directory = new TempDirectory();
        var query = new QuerySetting { MethodName = "Refresh", Target = "SELECT 1", SourceType = QuerySourceType.Text, GenerateHooks = true };
        var (_, code, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, new(query.Target, [], []));
        string implementation = before ? """
static partial void BeforeRefresh(DbConnection connection)
{
    throw new System.InvalidOperationException("Rejected");
}
""" : """
public static bool Disposed;
static partial void AfterRefresh(DbCommand command)
{
    command.Disposed += static (_, _) => Disposed = true;
    command.Dispose();
    throw new System.InvalidOperationException("Rejected");
}
""";
        Assembly assembly = GeneratedAssemblyCompiler.Compile(code, support, $$"""
using System.Data.Common;
namespace TestApp.Generated;
public static partial class DbCommands
{
    {{implementation}}
}
""");
        Type factory = assembly.GetType("TestApp.Generated.DbCommands")!;
        using var connection = new SqlConnection();
        var failure = Assert.Throws<TargetInvocationException>(() => factory.GetMethod(query.MethodName)!.Invoke(null, [before ? null : connection]));
        Assert.Equal("Rejected", failure.InnerException!.Message);
        if (!before)
        {
            Assert.Equal(true, factory.GetField("Disposed")!.GetValue(null));
        }
    }

    [Fact]
    public async Task Before_hook_changes_an_omitted_optional_argument_before_real_sql_execution()
    {
        using var directory = new TempDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY); INSERT INTO Items VALUES (1), (2);";
            await setup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var query = new QuerySetting { MethodName = "Load", Target = "SELECT Id FROM Items WHERE @Id IS NULL OR Id = @Id;", SourceType = QuerySourceType.Text, GenerateHooks = true, Parameters = [new() { Name = "@Id", DefaultValue = "null" }] };
        var settings = new ExtensionSettings { ConnectionTarget = connection.ConnectionString, Database = DatabaseType.Sqlite, ClassName = "DbCommands", Namespace = "Generated", Queries = [query] };
        settings.SetProjectDirectory(directory.Path);
        string output = await MainClassGenerator.GenerateClassAsync(SqliteDatabaseProvider.Instance.SchemaDiscoverer, connection, settings, "Generated", TestContext.Current.CancellationToken);
        Assembly assembly = GeneratedAssemblyCompiler.Compile(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory.Path, ".PowerTools.rinku.cs"), TestContext.Current.CancellationToken), """
using System.Data.Common;
namespace Generated;
public static partial class DbCommands
{
    static partial void BeforeLoad(DbConnection connection, ref object? Id)
    {
        Id = 2L;
    }
}
public static class Caller
{
    public static DbCommand Load(DbConnection connection) => connection.Load();
}
""");
        await using var command = (DbCommand)assembly.GetType("Generated.Caller")!.GetMethod("Load")!.Invoke(null, [connection])!;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disabled_flag_is_omitted_and_escaped_method_names_support_external_hooks()
    {
        using var directory = new TempDirectory();
        var query = new QuerySetting { MethodName = "@event", Target = "SELECT 1", SourceType = QuerySourceType.Text };
        Assert.DoesNotContain(nameof(QuerySetting.GenerateHooks), JsonSerializer.Serialize(query));
        var (_, plain, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, new(query.Target, [], []));
        Assert.DoesNotContain("partial void", plain);
        query.GenerateHooks = true;
        Assert.True(JsonSerializer.Deserialize<QuerySetting>(JsonSerializer.Serialize(query))!.GenerateHooks);
        var (_, code, _) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, new(query.Target, [], []));
        Assembly assembly = GeneratedAssemblyCompiler.Compile(code, support, """
using System.Data.Common;
namespace TestApp.Generated;
public static partial class DbCommands
{
    public static int BeforeCalls;
    static partial void Beforeevent(DbConnection connection)
    {
        BeforeCalls++;
    }
    static partial void Afterevent(DbCommand command)
    {
        command.CommandTimeout = 13;
    }
}
""");
        using var connection = new SqlConnection();
        using var command = (DbCommand)assembly.GetType("TestApp.Generated.DbCommands")!.GetMethod("event")!.Invoke(null, [connection])!;
        Assert.Equal(13, command.CommandTimeout);
        Assert.Equal(1, assembly.GetType("TestApp.Generated.DbCommands")!.GetField("BeforeCalls")!.GetValue(null));
    }

    [Fact]
    public async Task Overloaded_factories_share_matching_hooks_and_keep_disabled_overloads_unhooked()
    {
        using var directory = new TempDirectory();
        var settings = new ExtensionSettings
        {
            ConnectionTarget = "unused",
            OutputPath = "Generated",
            ClassName = "DbCommands",
            Queries =
            [
                new() { MethodName = "Refresh", Target = "SELECT @Id", SourceType = QuerySourceType.Text, GenerateHooks = true },
                new() { MethodName = "Refresh", Target = "SELECT @Id, @Count", SourceType = QuerySourceType.Text, GenerateHooks = true },
                new() { MethodName = "Refresh", Target = "SELECT @BigId", SourceType = QuerySourceType.Text, GenerateHooks = true },
                new() { MethodName = "Refresh", Target = "SELECT @Enabled", SourceType = QuerySourceType.Text }
            ]
        };
        settings.SetProjectDirectory(directory.Path);
        var discoverer = new SelectiveSchemaDiscoverer(query => new(query.Target, query.Target switch
        {
            "SELECT @Id" => [new("@Id", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0)],
            "SELECT @Id, @Count" => [new("@Id", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0), new("@Count", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0)],
            "SELECT @BigId" => [new("@BigId", DbType.Int64, false, 0, ParameterDirection.Input, 0, 0)],
            _ => [new("@Enabled", DbType.Boolean, false, 0, ParameterDirection.Input, 0, 0)]
        }, []));
        using var connection = new SqlConnection();
        string output = await MainClassGenerator.GenerateClassAsync(discoverer, connection, settings, "TestApp", TestContext.Current.CancellationToken);
        Assembly assembly = GeneratedAssemblyCompiler.Compile(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory.Path, ".PowerTools.rinku.cs"), TestContext.Current.CancellationToken), """
using System.Data.Common;
namespace TestApp.Generated;
public static partial class DbCommands
{
    public static int CommonBeforeCalls;
    public static int IntegerBeforeCalls;
    public static int LongBeforeCalls;
    public static int AfterCalls;
    static partial void BeforeRefresh(DbConnection connection)
    {
        CommonBeforeCalls++;
    }
    static partial void BeforeRefresh(DbConnection connection, ref int Id)
    {
        IntegerBeforeCalls++;
        Id++;
    }
    static partial void BeforeRefresh(DbConnection connection, ref long BigId)
    {
        LongBeforeCalls++;
        BigId += 2;
    }
    static partial void AfterRefresh(DbCommand command)
    {
        AfterCalls++;
        command.CommandTimeout = 17;
    }
}
public static class Caller
{
    public static DbCommand First(DbConnection connection) => connection.Refresh(10);
    public static DbCommand Second(DbConnection connection) => connection.Refresh(20, out _);
    public static DbCommand Third(DbConnection connection) => connection.Refresh(30L);
    public static DbCommand Disabled(DbConnection connection) => connection.Refresh(true);
}
""");
        Type caller = assembly.GetType("TestApp.Generated.Caller")!;
        using var first = (DbCommand)caller.GetMethod("First")!.Invoke(null, [connection])!;
        using var second = (DbCommand)caller.GetMethod("Second")!.Invoke(null, [connection])!;
        using var third = (DbCommand)caller.GetMethod("Third")!.Invoke(null, [connection])!;
        using var disabled = (DbCommand)caller.GetMethod("Disabled")!.Invoke(null, [connection])!;
        Assert.Equal(11, first.Parameters[0].Value);
        Assert.Equal(21, second.Parameters[0].Value);
        Assert.Equal(32L, third.Parameters[0].Value);
        Assert.Equal(ParameterDirection.Output, second.Parameters[1].Direction);
        Assert.Equal(true, disabled.Parameters[0].Value);
        Assert.Equal(17, first.CommandTimeout);
        Assert.Equal(17, second.CommandTimeout);
        Assert.Equal(17, third.CommandTimeout);
        Type factory = assembly.GetType("TestApp.Generated.DbCommands")!;
        Assert.Equal(3, factory.GetField("CommonBeforeCalls")!.GetValue(null));
        Assert.Equal(2, factory.GetField("IntegerBeforeCalls")!.GetValue(null));
        Assert.Equal(1, factory.GetField("LongBeforeCalls")!.GetValue(null));
        Assert.Equal(3, factory.GetField("AfterCalls")!.GetValue(null));
    }

    private static DiscoveredSchema Schema(string sql) => new(sql,
    [
        new("@name", DbType.String, true, 100, ParameterDirection.Input, 0, 0),
        new("@bytes", DbType.Binary, true, 100, ParameterDirection.Input, 0, 0),
        new("@value", DbType.Int64, true, 0, ParameterDirection.InputOutput, 0, 0),
        new("@count", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0)
    ], []);
}
