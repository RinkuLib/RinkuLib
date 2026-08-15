using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class AdvancedRegistrationDocumentationTests {
    private sealed record Result<T>(T Value) : IDbReadable;
    private sealed record ReadableChild(int Id) : IDbReadable;
    private sealed record ReadableParent(ReadableChild Child) : IDbReadable;
    private sealed record DirectlyRegistered(int Id);
    private readonly record struct DbValue<T>([NoName] T Value);
    private sealed record AuditEntry(int Id, DbValue<string> Actor);
    private sealed record Line(int Id, decimal Price) : IDbReadable;
    private sealed record Order(int Id, List<Line> Lines);

    [Fact]
    [DocumentationExample("index.md", "foundation-composition")]
    public void Result_mapping_composes_cardinality_objects_and_multi_row_members() {
        ColumnInfo[] columns = [new("Id", typeof(int), false), new("LinesId", typeof(int), false), new("LinesPrice", typeof(decimal), false)];
        using var reader = Rows.Reader(columns, [1, 10, 1.25m], [1, 11, 2.50m]);
        ITypeParser<Single<Order>> parser = TypeParser.GetTypeParser<Single<Order>>(columns);

        Assert.True(reader.Read());
        Order order = parser.Parse(reader).Result;
        Assert.Equal(1, order.Id);
        Assert.Equal([new Line(10, 1.25m), new Line(11, 2.50m)], order.Lines);
    }

    [Fact]
    [DocumentationExample("registration.md", "readable-registration")]
    public void Marker_and_direct_registration_make_types_available() {
        var parent = Rows.ParseOne<ReadableParent>([new("ChildId", typeof(int), false)], 7);

        Assert.Equal(new ReadableParent(new ReadableChild(7)), parent);
        Assert.Same(TypeParsingInfo.GetOrAdd<DirectlyRegistered>(), TypeParsingInfo.Get(typeof(DirectlyRegistered)));
    }

    [Fact]
    [DocumentationExample("registration.md", "mapped-wrapper")]
    public void An_open_generic_mapping_registers_a_wrapper_at_nested_slots() {
        Type wrapper = typeof(DbValue<>);
        TypeParsingInfo.TryRemove(wrapper, out _);
        try {
            TypeParsingInfo.GetOrAdd(wrapper);
            var entry = Rows.ParseOne<AuditEntry>([new("Id", typeof(int), false), new("Actor", typeof(string), false)], 1, "Ada");
            Assert.Equal(new AuditEntry(1, new DbValue<string>("Ada")), entry);

            ColumnInfo[] columns = [new("Value", typeof(string), false)];
            using var reader = Rows.Reader(columns, ["red"], ["blue"]);
            ITypeParser<DbValue<List<string>>> parser = TypeParser.GetTypeParser<DbValue<List<string>>>(columns);
            Assert.True(reader.Read());
            Assert.Equal(["red", "blue"], parser.Parse(reader).Result.Value);
        }
        finally {
            TypeParsingInfo.TryRemove(wrapper, out _);
        }
    }

    [Fact]
    [DocumentationExample("registration.md", "generic-registration")]
    public void Exact_generic_registration_wins_over_the_open_registration() {
        Type openType = typeof(Result<>);
        Type exactType = typeof(Result<int>);
        TypeParsingInfo.TryRemove(openType, out _);
        TypeParsingInfo.TryRemove(exactType, out _);
        try {
            TypeParsingInfo allResults = TypeParsingInfo.GetOrAdd(openType);
            TypeParsingInfo intResult = TypeParsingInfo.GetOrAdd<Result<int>>(saveAsGenericDefinitionWhenGeneric: false);

            Assert.Same(allResults, TypeParsingInfo.Get(typeof(Result<string>)));
            Assert.Same(intResult, TypeParsingInfo.Get(exactType));
            Assert.NotSame(allResults, intResult);
        }
        finally {
            TypeParsingInfo.TryRemove(exactType, out _);
            TypeParsingInfo.TryRemove(openType, out _);
        }
    }
}
