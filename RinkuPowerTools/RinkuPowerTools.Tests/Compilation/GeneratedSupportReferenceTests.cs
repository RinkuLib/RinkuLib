using System.Reflection;
using System.Data;
using Rinku.Mapping;
using RinkuPowerTools.Tests.Infrastructure;

namespace RinkuPowerTools.Tests.Compilation;

public class GeneratedSupportReferenceTests
{
    [Fact]
    public async Task GeneratedSupportAndCommandsCompileWithoutRinkuReference()
    {
        using var temp = new TempDirectory();
        var (_, commandCode, supportCode) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            new QuerySetting { MethodName = "GetAlbums", Target = "SELECT 1", SourceType = QuerySourceType.Text },
            new DiscoveredSchema("SELECT 1", [], [new("Id", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0), new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)]));

        Assembly assembly = GeneratedAssemblyCompiler.Compile(supportCode, commandCode);

        Assert.NotNull(assembly.GetType("TestApp.RinkuPowerTools"));
        Assert.NotNull(assembly.GetType("TestApp.Generated.DbCommands"));
        Type schemaType = assembly.GetType("RinkuPowerTools.Generated.SchemaAttribute")!;
        object schema = Assert.Single(assembly.GetType("TestApp.Generated.GetAlbumsResult")!.GetCustomAttributes(schemaType));
        Assert.IsType<DateTime>(schemaType.GetProperty("LastUpdated")!.GetValue(schema));
    }

    [Fact]
    public async Task ExistingSchemaAttributesRemainDiscoverable()
    {
        using var temp = new TempDirectory();
        var query = new QuerySetting { MethodName = "GetAlbums", Target = "SELECT 1", SourceType = QuerySourceType.Text };
        var (_, commandCode, supportCode) = await GeneratorTestHelper.GenerateAsync(
            temp.Path,
            query,
            new DiscoveredSchema("SELECT 1", [], [new("Id", DbType.Int32, false, 0, ParameterDirection.Output, 0, 0), new("Title", DbType.String, false, 0, ParameterDirection.Output, 0, 0)]));
        var settings = new ExtensionSettings { ConnectionTarget = "unused", OutputPath = "Generated", ClassName = "DbCommands" };
        settings.SetProjectDirectory(temp.Path);
        string modelPath = await FromResultSetGenerator.GenerateClassAsync("TestApp", temp.Path, "GeneratedAlbum", settings, query, TestContext.Current.CancellationToken);
        string modelCode = await File.ReadAllTextAsync(modelPath, TestContext.Current.CancellationToken);

        const string schemaCode = """
            namespace TestApp.Schemas;
            public static class AlbumSchema
            {
                public static void Read(int Id, string Title) { }
            }
            """;
        const string resultCode = """
            using Rinku.Mapping;
            namespace TestApp.Generated;
            [BasedOn(typeof(TestApp.Schemas.AlbumSchema), nameof(TestApp.Schemas.AlbumSchema.Read), "2026-10-12T10:11:12.345-04:00")]
            public sealed record Album(int Id, string Title);
            """;

        Assembly assembly = GeneratedAssemblyCompiler.Compile(typeof(SchemaAttribute).Assembly, supportCode, commandCode, schemaCode, resultCode, modelCode);
        Type resultType = assembly.GetType("TestApp.Generated.Album") ?? throw new InvalidOperationException();
        (Type ResultType, MemberInfo Member) schema = Assert.Single(resultType.GetSchemas());

        Assert.Same(resultType, schema.ResultType);
        Assert.Equal("Read", schema.Member.Name);
        var methodLink = resultType.GetCustomAttribute<BasedOnAttribute>()!;
        Assert.Equal(new DateTime(2026, 10, 12, 14, 11, 12, 345, DateTimeKind.Utc), methodLink.LastUpdated);
        Type generatedType = assembly.GetType("TestApp.GeneratedAlbum")!;
        Assert.Equal(DateTimeKind.Utc, generatedType.GetCustomAttribute<BasedOnAttribute>()!.LastUpdated!.Value.Kind);
        Assert.Equal("GetAlbumsResult", Assert.Single(generatedType.GetSchemas()).Member.DeclaringType!.Name);
    }
}
