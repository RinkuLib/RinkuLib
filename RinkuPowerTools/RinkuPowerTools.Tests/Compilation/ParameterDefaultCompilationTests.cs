using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Compilation;

public class ParameterDefaultCompilationTests
{
    [Fact]
    public void DefaultOnlyConfigurationRoundTripsWithoutOtherCorrections()
    {
        var correction = new ParameterOverride { Name = "@Id", DefaultValue = "null" };
        string json = JsonSerializer.Serialize(correction);
        Assert.DoesNotContain(nameof(ParameterOverride.Type), json);
        Assert.DoesNotContain(nameof(ParameterOverride.IsNullable), json);
        Assert.Equal("null", JsonSerializer.Deserialize<ParameterOverride>(json)!.DefaultValue);
        correction.DefaultValue = null;
        Assert.DoesNotContain(nameof(ParameterOverride.DefaultValue), JsonSerializer.Serialize(correction));
    }

    [Fact]
    public async Task OmittedArgumentsBindConfiguredValuesWithOptionalHooks()
    {
        using var directory = new TempDirectory();
        List<ParameterMetadata> parameters =
        [
            new("@Id", DbType.Int64, true, 0, ParameterDirection.Input, 0, 0) { DefaultValue = "null" },
            new("@Limit", DbType.Int32, false, 0, ParameterDirection.Input, 0, 0) { DefaultValue = "10" },
            new("@Enabled", DbType.Boolean, false, 0, ParameterDirection.Input, 0, 0) { DefaultValue = "true" },
            new("@Text", DbType.String, false, 100, ParameterDirection.Input, 0, 0) { DefaultValue = "\"a\\\"b\"" },
            new("@Amount", DbType.Decimal, false, 0, ParameterDirection.Input, 18, 2) { DefaultValue = "1.5m" },
            new("@Token", DbType.Guid, false, 0, ParameterDirection.Input, 0, 0) { DefaultValue = "default(Guid)" }
        ];
        var query = new QuerySetting { MethodName = "Load", Target = "SELECT 1", SourceType = QuerySourceType.Text, GenerateHooks = true };
        var (_, code, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, new(query.Target, parameters, []));
        Assembly assembly = GeneratedAssemblyCompiler.Compile(code, support, """
using System.Data.Common;
using TestApp.Generated;
public static class Caller {
    public static DbCommand Omitted(DbConnection connection) => connection.Load();
    public static DbCommand Explicit(DbConnection connection) => connection.Load(7, 20, false, "override", 2m, default);
}
""");
        using var connection = new SqlConnection();
        using var omitted = (DbCommand)assembly.GetType("Caller")!.GetMethod("Omitted")!.Invoke(null, [connection])!;
        Assert.Equal(DBNull.Value, omitted.Parameters[0].Value);
        Assert.Equal(10, omitted.Parameters[1].Value);
        Assert.Equal(true, omitted.Parameters[2].Value);
        Assert.Equal("a\"b", omitted.Parameters[3].Value);
        Assert.Equal(1.5m, omitted.Parameters[4].Value);
        Assert.Equal(Guid.Empty, omitted.Parameters[5].Value);
        using var explicitValues = (DbCommand)assembly.GetType("Caller")!.GetMethod("Explicit")!.Invoke(null, [connection])!;
        Assert.Equal(7L, explicitValues.Parameters[0].Value);
        Assert.Equal(20, explicitValues.Parameters[1].Value);
        Assert.Equal(false, explicitValues.Parameters[2].Value);
        Assert.Equal("override", explicitValues.Parameters[3].Value);
        Assert.Equal("@Id", omitted.Parameters[0].ParameterName);
        Assert.Equal("@Token", omitted.Parameters[5].ParameterName);
    }

    [Fact]
    public async Task DefaultOnlyCorrectionDiscoversCompilesAndExecutesFullAndFilteredSql()
    {
        using var directory = new TempDirectory();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Items VALUES (1, 'First'), (2, 'Second');";
            await setup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var query = JsonSerializer.Deserialize<QuerySetting>("""
{"MethodName":"LoadItems","SQLQuery":"SELECT Id, Name FROM Items WHERE @Id IS NULL OR Id = @Id ORDER BY Id;","Parameters":[{"Name":"@Id","DefaultValue":"null"}]}
""")!;
        var settings = new ExtensionSettings { ConnectionTarget = connection.ConnectionString, Database = DatabaseType.Sqlite, ClassName = "DbCommands", Namespace = "Generated", Queries = [query] };
        settings.SetProjectDirectory(directory.Path);
        string output = await MainClassGenerator.GenerateClassAsync(SqliteDatabaseProvider.Instance.SchemaDiscoverer, connection, settings, "Generated", TestContext.Current.CancellationToken);
        Assembly assembly = GeneratedAssemblyCompiler.Compile(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(Path.Combine(directory.Path, ".PowerTools.rinku.cs"), TestContext.Current.CancellationToken), """
using System.Data.Common;
using Generated;
public static class Caller {
    public static DbCommand All(DbConnection connection) => connection.LoadItems();
    public static DbCommand One(DbConnection connection) => connection.LoadItems(2L);
}
""");
        await using var all = (DbCommand)assembly.GetType("Caller")!.GetMethod("All")!.Invoke(null, [connection])!;
        await using (var reader = await all.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2L, reader.GetInt64(0));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        await using var one = (DbCommand)assembly.GetType("Caller")!.GetMethod("One")!.Invoke(null, [connection])!;
        await using var filtered = await one.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await filtered.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2L, filtered.GetInt64(0));
        Assert.False(await filtered.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ParameterDirection.Input, "null", true)]
    [InlineData(ParameterDirection.Output, "10", false)]
    [InlineData(ParameterDirection.InputOutput, "10", false)]
    [InlineData(ParameterDirection.Input, "", false)]
    [InlineData(ParameterDirection.Input, "null); public void Inject() {}", false)]
    public async Task InvalidDefaultsOrOrderingFailWithoutReordering(ParameterDirection direction, string value, bool requiredAfter)
    {
        using var directory = new TempDirectory();
        List<ParameterMetadata> parameters = [new("@First", DbType.Int32, true, 0, direction, 0, 0) { DefaultValue = value }];
        if (requiredAfter)
            parameters.Add(new("@Second", DbType.String, false, 20, ParameterDirection.Input, 0, 0));
        var query = new QuerySetting { MethodName = "Load", Target = "SELECT 1", SourceType = QuerySourceType.Text };
        var (_, code, support) = await GeneratorTestHelper.GenerateAsync(directory.Path, query, new(query.Target, parameters, []));
        Assert.Contains("#error Query generation failed", code);
        Assert.DoesNotContain("public static DbCommand Load(", code);
        Assert.Throws<InvalidOperationException>(() => GeneratedAssemblyCompiler.Compile(code, support));
    }
}
