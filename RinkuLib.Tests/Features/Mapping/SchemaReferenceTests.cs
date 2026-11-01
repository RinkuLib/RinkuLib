using System.Data;
using System.Reflection;
using Rinku.Mapping;
using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.Features.Mapping;

/// <summary>Tests schema-reference discovery and parser materialization.</summary>
public sealed class SchemaReferenceTests {
    /// <summary>Contains overloaded schema methods used by the schema-reference tests.</summary>
    public static class Schemas {
        /// <summary>Defines the two-column album schema.</summary>
        public static void Album(int Id, string Title) { }
        /// <summary>Defines the one-column album schema used to test overload discovery.</summary>
        public static void Album(int Id) { }
        /// <summary>Defines the single album schema used by parser materialization tests.</summary>
        public static void SingleAlbum(int Id, string Title) { }
    }

    /// <summary>Defines a constructor-based schema.</summary>
    public sealed class ConstructorSchema {
        /// <summary>Creates the constructor-based schema shape.</summary>
        public ConstructorSchema(int Id, string Title) { }
    }

    [BasedOn(typeof(Schemas), null, nameof(Schemas.SingleAlbum))]
    private sealed record Album(int Id, string Title);

    [BasedOn(typeof(Schemas), null, nameof(Schemas.SingleAlbum))]
    [BasedOn(typeof(AlbumSchema))]
    private sealed record MultiAlbum(int Id, string Title);

    [BasedOn(typeof(ConstructorSchema))]
    private sealed record ConstructorAlbum(int Id, string Title);

    [ExternalSchema]
    private sealed record ExternalAlbum(int Id, string Title);

    private sealed class ExternalSchemaAttribute : Attribute, IEnumerable<MemberInfo> {
        public IEnumerator<MemberInfo> GetEnumerator() => ((IEnumerable<MemberInfo>)typeof(ConstructorSchema).GetConstructors()).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Schema("2026-08-21T10:00Z")]
    private sealed record VersionOnlyAlbum(int Id, string Title);

    private sealed record AlbumSchema(int Id, string Title);

    [BasedOn(typeof(Schemas), null, nameof(Schemas.Album))]
    private sealed record OverloadedAlbum(int Id, string Title);

    [Fact]
    public void Timestamp_is_the_second_argument_and_method_selection_is_the_third() {
        var positional = new BasedOnAttribute(typeof(Schemas), "2026-08-21T10:00Z", nameof(Schemas.SingleAlbum));
        var named = new BasedOnAttribute(methodName: nameof(Schemas.SingleAlbum), lastUpdated: "2026-08-21T10:00Z", declaringType: typeof(Schemas));
        Assert.Equal(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc), positional.LastUpdated);
        Assert.Equal(named.LastUpdated, positional.LastUpdated);
        Assert.Equal(named.ToArray(), positional.ToArray());
        Assert.Single(positional);
        Assert.IsAssignableFrom<ConstructorInfo>(Assert.Single(new BasedOnAttribute(typeof(ConstructorSchema), "2026-08-21T10:00Z")));
    }

    [Fact]
    public void Type_enumeration_returns_result_type_and_schema_member() {
        (Type resultType, MemberInfo member) schema = Assert.Single(typeof(Album).GetSchemas());

        Assert.Same(typeof(Album), schema.resultType);
        Assert.IsAssignableFrom<MethodBase>(schema.member);
    }

    [Fact]
    public void Schema_member_builds_generic_parser_and_materializes_rows() {
        (Type resultType, MemberInfo member) schema = Assert.Single(typeof(Album).GetSchemas());
        Assert.Same(typeof(Album), schema.resultType);

        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Title", typeof(string));
        table.Rows.Add(7, "Seven");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        using ITypeParser<Album> parser = TypeParser.GetTypeParser<Album>(schema.member);
        Album result = parser.Parse(reader).Result;

        Assert.Equal(7, result.Id);
        Assert.Equal("Seven", result.Title);
    }

    [Fact]
    public void Schema_member_builds_runtime_parser() {
        (Type resultType, MemberInfo member) schema = Assert.Single(typeof(Album).GetSchemas());

        using ITypeParser parser = TypeParser.GetTypeParser(schema.resultType, schema.member);

        Assert.Equal(typeof(Album), parser.Type);
    }

    [Fact]
    public void Several_schema_references_preserve_order() {
        Assert.Collection(typeof(MultiAlbum).GetSchemas(),
            schema => {
                Assert.Same(typeof(MultiAlbum), schema.ResultType);
                Assert.IsAssignableFrom<MethodBase>(schema.Member);
            },
            schema => {
                ConstructorInfo constructor = Assert.IsAssignableFrom<ConstructorInfo>(schema.Member);
                Assert.Equal(typeof(AlbumSchema), constructor.DeclaringType);
            });
    }

    [Fact]
    public void Compiler_resolves_overloaded_method_cref() {
        Assert.Equal(2, typeof(OverloadedAlbum).GetSchemas().Count());
        Assert.Contains(typeof(OverloadedAlbum).GetSchemas(), schema => {
            ParameterInfo[] parameters = Assert.IsAssignableFrom<MethodBase>(schema.Member).GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(string);
        });
    }

    [Fact]
    public void Constructor_cref_dispatches_through_member_info_and_materializes_rows() {
        (Type resultType, MemberInfo member) schema = Assert.Single(typeof(ConstructorAlbum).GetSchemas());
        ConstructorInfo constructor = Assert.IsAssignableFrom<ConstructorInfo>(schema.member);
        Assert.Equal(typeof(ConstructorSchema), constructor.DeclaringType);

        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Title", typeof(string));
        table.Rows.Add(8, "Eight");
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        using ITypeParser<ConstructorAlbum> parser = TypeParser.GetTypeParser<ConstructorAlbum>(schema.member);
        ConstructorAlbum result = parser.Parse(reader).Result;

        Assert.Equal(8, result.Id);
        Assert.Equal("Eight", result.Title);
    }

    [Fact]
    public void Independent_attribute_selects_schema_members_through_its_capability() {
        (Type resultType, MemberInfo member) schema = Assert.Single(typeof(ExternalAlbum).GetSchemas());

        Assert.Same(typeof(ExternalAlbum), schema.resultType);
        Assert.IsAssignableFrom<ConstructorInfo>(schema.member);
        Assert.Equal(typeof(ConstructorSchema), schema.member.DeclaringType);
    }

    [Fact]
    public void Assembly_enumeration_contains_documented_types() {
        Assert.Contains(typeof(Album).Assembly.GetSchemas(), x => x.ResultType == typeof(Album));
        Assert.Contains(typeof(Album).Assembly.GetSchemas(), x => x.ResultType == typeof(ConstructorAlbum));
    }

    [Fact]
    public void All_schema_enumeration_contains_documented_types_from_loaded_assemblies() {
        Assert.Contains(SchemaReference.GetAllSchemas(), x => x.ResultType == typeof(Album));
        Assert.Contains(SchemaReference.GetAllSchemas(), x => x.ResultType == typeof(ConstructorAlbum));
    }

    [Fact]
    public void Schema_lookup_does_not_load_additional_assemblies() {
        HashSet<string> before = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.FullName).Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        Assert.Single(typeof(Album).GetSchemas());

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.FullName is string fullName)
                Assert.Contains(fullName, before);
    }

    [Fact]
    public void Member_without_schema_returns_empty() {
        Assert.Empty(typeof(SchemaReferenceTests).GetSchemas());
    }

    [Fact]
    public void Version_metadata_alone_does_not_create_a_runtime_schema_reference() {
        Assert.Empty(typeof(VersionOnlyAlbum).GetSchemas());
    }
}
