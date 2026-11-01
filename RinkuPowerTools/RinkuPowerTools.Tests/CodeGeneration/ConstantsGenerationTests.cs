using System.Text.Json;
using Microsoft.Data.Sqlite;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.CodeGeneration;

public sealed class ConstantsGenerationTests {
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ExtensionSettings Settings(ConstantsSource source, string directory) {
        var settings = new ExtensionSettings { ConnectionSourceType = ConnectionSourceType.RawConnectionString, ConnectionTarget = "Data Source=:memory:", ConnectionString = "Data Source=:memory:", Database = DatabaseType.Sqlite, Namespace = "Generated", Constants = source };
        settings.SetProjectDirectory(directory); return settings;
    }
    private static void Compiles(string code) => GeneratedAssemblyCompiler.Compile(code);

    [Fact]
    public void Implicit_nested_containers_mixed_constants_descriptions_and_keywords_compile() {
        var code = ConstantsGenerator.GenerateCode([
            new("Reader", "1", "byte", "Read \"access\"\nonly", "Application.Roles"), new("Administrator", "300", "long", null, "Application.Roles"),
            new("Title", "Rinku\n\"demo\"", "string", null, "Application.Settings"), new("Empty", "", "string", null, "Application.Settings"),
            new("Enabled", "true", "bool", null, "Application.Settings"), new("class", "-1", "int", null, "Other"),
            new("Roles", null, null, "Roles", "Application")
        ]);
        Assert.Contains("enum Roles : long", code); Assert.Contains("static class Settings", code);
        Assert.Contains("string Empty = \"\"", code); Assert.Contains("@class = -1", code);
        Assert.Contains("Description(\"Roles\")", code); Compiles(code);
    }

    [Theory]
    [InlineData("sbyte", "-128", "byte", "255", "short")]
    [InlineData("short", "-32768", "ushort", "65535", "int")]
    [InlineData("int", "-2147483648", "uint", "4294967295", "long")]
    [InlineData("long", "-9223372036854775808", "long", "9223372036854775807", "long")]
    [InlineData("ulong", "18446744073709551615", "long", "1", "ulong")]
    [InlineData("byte", "1", "long", "2", "long")]
    public void Signed_unsigned_ranges_and_largest_declared_types_compile(string firstType, string firstValue, string secondType, string secondValue, string expected) {
        var code = ConstantsGenerator.GenerateCode([new("First", firstValue, firstType, null, "Numbers"), new("Second", secondValue, secondType, null, "Numbers")]);
        Assert.Contains("enum Numbers : " + expected, code); Compiles(code);
    }

    [Fact]
    public void Duplicate_enum_values_are_valid_and_incompatible_integer_ranges_become_fields() {
        Compiles(ConstantsGenerator.GenerateCode([new("A", "1", "int", null, "Aliases"), new("B", "1", "int", null, "Aliases")]));
        var code = ConstantsGenerator.GenerateCode([new("Negative", "-1", "long", null, "Numbers"), new("Huge", "18446744073709551615", "ulong", null, "Numbers")]);
        Assert.Contains("static class Numbers", code); Compiles(code);
    }

    [Fact]
    public void Scalar_literals_and_containers_with_both_values_and_subgroups_compile() {
        var code = ConstantsGenerator.GenerateCode([new("Id", "3", "int", null, "Root"), new("Name", "123", "System.String", null, "Root.Nested"), new("Char", "'", "char", null, "Root"), new("Rate", "1.23", "decimal", null, "Root"), new("Float", "0.1", "float", null, "Root"), new("Double", "NaN", "double", null, "Root"), new("Empty", null, null, null, null)]);
        Assert.Contains("static class Root", code); Assert.Contains("string Name = \"123\"", code); Compiles(code);
    }

    [Theory]
    [InlineData("bad-name", null, null, null)]
    [InlineData("Leaf", "1", "int", null)]
    [InlineData("Leaf", "256", "byte", "Root")]
    [InlineData("Leaf", "1", "Unknown", "Root")]
    [InlineData("Leaf", "1", null, "Root")]
    [InlineData("Leaf", null, "int", "Root")]
    [InlineData("Leaf", "1", "int", "Root..Group")]
    [InlineData("Loop", null, null, "Loop")]
    [InlineData("value__", "1", "int", "Root")]
    public void Invalid_names_paths_self_references_roots_types_and_values_fail(string name, string? value, string? type, string? parent) {
        Assert.Throws<InvalidOperationException>(() => ConstantsGenerator.GenerateCode([new(name, value, type, null, parent)]));
    }

    [Fact]
    public void Duplicate_paths_and_value_parent_conflicts_fail_independent_of_row_order() {
        var leaf = new ConstantDefinition("Leaf", "1", "int", null, "Root");
        Assert.Throws<InvalidOperationException>(() => ConstantsGenerator.GenerateCode([leaf, leaf]));
        var child = new ConstantDefinition("Child", "2", "int", null, "Root.Leaf");
        Assert.Throws<InvalidOperationException>(() => ConstantsGenerator.GenerateCode([leaf, child]));
        Assert.Throws<InvalidOperationException>(() => ConstantsGenerator.GenerateCode([child, leaf]));
    }

    [Fact]
    public void Parent_paths_and_implicit_structure_are_order_independent() {
        ConstantDefinition[] rows = [new("Value", "4", "int", null, "Root.Inner"), new("Inner", null, null, "Group", "Root"), new("Root", null, null, "Root", null)];
        Assert.Equal(ConstantsGenerator.GenerateCode(rows), ConstantsGenerator.GenerateCode(Enumerable.Reverse(rows)));
    }

    [Fact]
    public async Task File_and_raw_SQL_are_reexecuted_and_refresh_updates_constants_in_the_command_output() {
        using var temp = new TempDirectory();
        using var connection = new SqliteConnection("Data Source=:memory:");
        string file = Path.Combine(temp.Path, "Constants.sql");
        string sql = "SELECT 'Reader' AS Name, '1' AS Value, 'int' AS Type, NULL AS Description, 'Roles' AS Parent";
        await File.WriteAllTextAsync(file, sql, Token);
        var settings = Settings(new(QuerySourceType.FromFile, "Constants.sql"), temp.Path);
        string output = await MainClassGenerator.GenerateClassAsync(new StaticSchemaDiscoverer(new("", [], [])), connection, settings, "Generated", Token);
        string initial = await File.ReadAllTextAsync(output, Token); Assert.Contains("Reader = 1", initial);
        Assert.DoesNotContain("class DbCommands", initial);
        GeneratedAssemblyCompiler.Compile(initial, await File.ReadAllTextAsync(Path.Combine(temp.Path, ".PowerTools.rinku.cs"), Token));
        await File.WriteAllTextAsync(file, sql.Replace("Reader", "Writer"), Token);
        await MainClassGenerator.GenerateClassAsync(new StaticSchemaDiscoverer(new("", [], [])), connection, settings, "Generated", Token);
        Assert.Contains("Writer = 1", await File.ReadAllTextAsync(output, Token));
        string commands = await File.ReadAllTextAsync(output, Token);
        settings.Constants = new(QuerySourceType.Text, "SELECT 'Invalid' AS Name, '2' AS Value, 'int' AS Type, NULL AS Description, NULL AS Parent");
        await Assert.ThrowsAsync<InvalidOperationException>(() => MainClassGenerator.GenerateClassAsync(new StaticSchemaDiscoverer(new("", [], [])), connection, settings, "Generated", Token));
        Assert.Equal(commands, await File.ReadAllTextAsync(output, Token));
        settings.Constants = new(QuerySourceType.Text, sql.Replace("Roles", "DbCommands"));
        settings.Queries.Add(new QuerySetting { MethodName = "Load", SourceType = QuerySourceType.Text, Target = "SELECT 1" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => MainClassGenerator.GenerateClassAsync(new StaticSchemaDiscoverer(new("", [], [])), connection, settings, "Generated", Token));
        Assert.Equal(commands, await File.ReadAllTextAsync(output, Token));
        settings.Queries.Clear();
        settings.Constants = null;
        await MainClassGenerator.GenerateClassAsync(new StaticSchemaDiscoverer(new("", [], [])), connection, settings, "Generated", Token);
        Assert.DoesNotContain("enum Roles", await File.ReadAllTextAsync(output, Token));
    }

    [Theory]
    [InlineData("SELECT 'Bad' AS Name, 1 AS Value, 'int' AS Type, NULL AS Description, 'Roles' AS Parent")]
    [InlineData("SELECT 'Bad' AS Name, '1' AS Value")]
    [InlineData("SELECT 'Bad' AS Name, '1' AS Value, 'int' AS Type, NULL AS Description, 'Roles' AS Parent; SELECT 'Extra'")]
    public async Task Incorrect_result_contracts_fail(string sql) {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConstantsGenerator.GenerateAsync(connection, Settings(new(QuerySourceType.Text, sql), Directory.GetCurrentDirectory()), Token));
    }

    [Fact]
    public void All_three_source_modes_round_trip_and_ambiguous_sources_or_old_config_fail() {
        foreach (var type in Enum.GetValues<QuerySourceType>()) {
            var settings = Settings(new ConstantsSource(type, "target"), Directory.GetCurrentDirectory());
            string json = JsonSerializer.Serialize(settings);
            Assert.Contains("\"" + settings.Constants!.PropertyName + "\":\"target\"", json);
            Assert.DoesNotContain("null", json); Assert.DoesNotContain("Definitions", json); Assert.DoesNotContain("\"Constants\":", json);
            var restored = JsonSerializer.Deserialize<ExtensionSettings>(json)!;
            Assert.Equal(type, restored.Constants!.ToQuery().SourceType); Assert.Equal("target", restored.Constants.ToQuery().Target);
        }
        foreach (string invalid in new[] { "\"ConstantsSQL\":\"sql\",\"ConstantsPath\":\"file\"", "\"ConstantsPath\":null", "\"ConstantsSQL\":\" \"", "\"Definitions\":{\"SQLFile\":\"file\"}" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExtensionSettings>("{\"RawConnectionString\":\"unused\"," + invalid + "}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExtensionSettings>("{\"RawConnectionString\":\"unused\",\"Enums\":[]}"));
    }

    [Fact]
    public async Task Stored_procedure_source_executes_against_SQL_Server_when_configured() {
        string? endpoint = Environment.GetEnvironmentVariable("RINKU_POWERTOOLS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        using var temp = new TempDirectory();
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(endpoint);
        await connection.OpenAsync(Token);
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE PROCEDURE #RinkuConstants AS SET NOCOUNT ON; SELECT CAST(N'Reader' AS nvarchar(128)) AS Name, CAST(N'-1' AS nvarchar(32)) AS Value, CAST(N'long' AS nvarchar(16)) AS Type, CAST(NULL AS nvarchar(max)) AS Description, CAST(N'Roles' AS nvarchar(128)) AS Parent;";
        await create.ExecuteNonQueryAsync(Token);
        var settings = Settings(new(QuerySourceType.StoredProcedure, "#RinkuConstants"), temp.Path);
        settings.Database = DatabaseType.SqlServer; settings.ConnectionString = endpoint;
        string code = await ConstantsGenerator.GenerateAsync(connection, settings, Token);
        Assert.Contains("enum Roles : long", code); Assert.Contains("Reader = -1L", code); Compiles(code);
    }
}
