using System.Reflection;
using Rinku.Mapping.Defaults;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class RegistrationInitializerTests {
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class CustomParamMakerAttribute : Attribute, IParamInfoMaker {
        public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer,
            string? name, object[] attributes, UsageFlags usageFlags, object? param)
            => new(Type, NullableTypeHandle.Instance, new NameComparer("FromMaker"));
    }

    private sealed class CustomParamHost {
        [CustomParamMaker]
        public string? Value { get; set; }
    }

    [Fact]
    public void A_param_initializer_receives_the_final_custom_maker_result_while_direct_construction_bypasses_it() {
        var previous = ParamInfo.RegistrationInitializer;
        ParamInfo? received = null;
        int calls = 0;
        ParamInfo.RegistrationInitializer = info => {
            calls++;
            received = info;
            info.NameComparer = new NameComparer("FromInitializer");
            info.NullColHandler = NotNullHandle.Instance;
        };
        try {
            var direct = new ParamInfo(typeof(string), NullableTypeHandle.Instance, new NameComparer("Direct"));
            Assert.Equal(0, calls);
            Assert.True(direct.NameComparer.Contains("Direct"));

            var created = ParamInfo.TryNew(typeof(CustomParamHost).GetProperty(nameof(CustomParamHost.Value))!);

            Assert.NotNull(created);
            Assert.Same(created, received);
            Assert.Equal(1, calls);
            Assert.True(created.NameComparer.Contains("FromInitializer"));
            Assert.False(created.NameComparer.Contains("FromMaker"));
            Assert.Same(NotNullHandle.Instance, created.NullColHandler);
        }
        finally {
            ParamInfo.RegistrationInitializer = previous;
        }
    }

    private readonly record struct ConventionalChild(int Id, string Name) : IDbReadable;
    private sealed record ConventionalParent(int ParentId, ConventionalChild? Child) : IDbReadable;

    [Fact]
    public void A_param_initializer_can_make_every_default_id_collapse_its_containing_object_on_null() {
        var childType = typeof(ConventionalChild);
        var parentType = typeof(ConventionalParent);
        var previous = ParamInfo.RegistrationInitializer;
        ColumnInfo[] columns = [new("ParentId", typeof(int), false), new("ChildId", typeof(int), true), new("ChildName", typeof(string), true)];
        TypeParsingInfo.TryRemove(childType, out _);
        TypeParsingInfo.TryRemove(parentType, out _);
        ParamInfo.RegistrationInitializer = info => {
            if (string.Equals(info.NameComparer.GetDefaultName(), "Id", StringComparison.OrdinalIgnoreCase))
                info.SetAbortOnNull(true);
        };
        try {
            var result = Rows.ParseOne<ConventionalParent>(columns, 1, DBNull.Value, DBNull.Value);

            Assert.Equal(1, result.ParentId);
            Assert.Null(result.Child);
        }
        finally {
            ParamInfo.RegistrationInitializer = previous;
            TypeParsingInfo.TryRemove(childType, out _);
            TypeParsingInfo.TryRemove(parentType, out _);
            TypeParser.Invalidate(columns, ParserInvalidationMode.InvalidateReferences);
        }
    }

    private sealed record ConstructionGrouped(int Id, string Label, List<int> Values) : IDbReadable;

    [Fact]
    public void A_construction_initializer_can_replace_inferred_grouping_with_an_id_key() {
        var type = typeof(ConstructionGrouped);
        var previous = MethodCtorInfo.RegistrationInitializer;
        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Label", typeof(string), false), new("Values", typeof(int), false)];
        TypeParsingInfo.TryRemove(type, out _);
        MethodCtorInfo.RegistrationInitializer = path => {
            if (path.TargetType != type)
                return;
            var id = path.MethodBase.GetParameters().Single(parameter => parameter.Name == "Id");
            path.GroupKey = new EqualityGroupingRule(id);
        };
        try {
            var direct = new MethodCtorInfo(type.GetConstructors()[0]);
            Assert.Null(direct.GroupKey);

            var parser = TypeParser.GetTypeParser<List<ConstructionGrouped>>(columns);
            using var reader = Rows.Reader(columns, [1, "first", 10], [1, "changed", 11], [2, "second", 20]);
            reader.Read();
            var result = parser.Parse(reader).Result;

            Assert.Equal(2, result.Count);
            Assert.Equal((1, "first"), (result[0].Id, result[0].Label));
            Assert.Equal([10, 11], result[0].Values);
            Assert.Equal((2, "second"), (result[1].Id, result[1].Label));
            Assert.Equal([20], result[1].Values);
        }
        finally {
            MethodCtorInfo.RegistrationInitializer = previous;
            TypeParsingInfo.TryRemove(type, out _);
            TypeParser.Invalidate(columns, ParserInvalidationMode.InvalidateReferences);
        }
    }

    private sealed class TypeGrouped : IDbReadable {
        public int Id { get; set; }
        public string Label { get; set; } = null!;
        public List<int> Values { get; set; } = [];
    }

    private sealed class ExplicitlyRegistered : IDbReadable;

    private sealed record CacheAnchor(int Value) : IDbReadable;
    private sealed record ConventionConfigured(int Id) : IDbReadable;

    [Fact]
    public void Initializing_new_metadata_does_not_invalidate_an_existing_parser() {
        var anchorType = typeof(CacheAnchor);
        var configuredType = typeof(ConventionConfigured);
        var previousParam = ParamInfo.RegistrationInitializer;
        var previousConstruction = MethodCtorInfo.RegistrationInitializer;
        var previousType = TypeParsingInfo.RegistrationInitializer;
        ColumnInfo[] anchorColumns = [new("Value", typeof(int), false)];
        ColumnInfo[] configuredColumns = [new("Id", typeof(int), false)];
        TypeParsingInfo.TryRemove(anchorType, out _);
        TypeParsingInfo.TryRemove(configuredType, out _);
        var anchor = TypeParser.GetTypeParser<CacheAnchor>(anchorColumns);
        ParamInfo.RegistrationInitializer = info => {
            if (string.Equals(info.NameComparer.GetDefaultName(), "Id", StringComparison.OrdinalIgnoreCase))
                info.SetAbortOnNull(true);
        };
        MethodCtorInfo.RegistrationInitializer = path => {
            if (path.TargetType == configuredType)
                path.Flags |= MethodCtorInfo.AdditionalFlags.CanCompleteWithMembers;
        };
        TypeParsingInfo.RegistrationInitializer = (type, info) => {
            if (type == configuredType && info is ICanUpdateGroupKey grouping)
                grouping.GroupKey = new EqualityGroupingRule(configuredType.GetProperty(nameof(ConventionConfigured.Id))!);
        };
        try {
            var configuredInfo = TypeParsingInfo.ForceGet(configuredType);
            Assert.NotEmpty(Assert.IsAssignableFrom<ICanProvideParamInfos>(configuredInfo).GetParamInfos());
            TypeParser.GetTypeParser<ConventionConfigured>(configuredColumns);

            Assert.Same(anchor, TypeParser.GetTypeParser<CacheAnchor>(anchorColumns));
        }
        finally {
            ParamInfo.RegistrationInitializer = previousParam;
            MethodCtorInfo.RegistrationInitializer = previousConstruction;
            TypeParsingInfo.RegistrationInitializer = previousType;
            TypeParsingInfo.TryRemove(anchorType, out _);
            TypeParsingInfo.TryRemove(configuredType, out _);
            TypeParser.Invalidate(anchorColumns, ParserInvalidationMode.InvalidateReferences);
            TypeParser.Invalidate(configuredColumns, ParserInvalidationMode.InvalidateReferences);
        }
    }

    [Fact]
    public void A_type_initializer_runs_before_publication_and_can_supply_a_member_grouping_key() {
        var type = typeof(TypeGrouped);
        var explicitType = typeof(ExplicitlyRegistered);
        var previous = TypeParsingInfo.RegistrationInitializer;
        bool wasAbsentDuringInitialization = false;
        int explicitCalls = 0;
        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Label", typeof(string), false), new("Values", typeof(int), false)];
        TypeParsingInfo.TryRemove(type, out _);
        TypeParsingInfo.TryRemove(explicitType, out _);
        TypeParsingInfo.RegistrationInitializer = (createdType, info) => {
            if (createdType == explicitType) {
                explicitCalls++;
                return;
            }
            if (createdType != type)
                return;
            wasAbsentDuringInitialization = TypeParsingInfo.Get(type) is null;
            var grouping = Assert.IsAssignableFrom<ICanUpdateGroupKey>(info);
            grouping.GroupKey = new EqualityGroupingRule(type.GetProperty(nameof(TypeGrouped.Id))!);
        };
        try {
            var explicitInfo = new DefaultTypeParsingInfo(explicitType);
            Assert.Same(explicitInfo, TypeParsingInfo.GetOrAdd(explicitType, explicitInfo, saveAsGenericDefinitionWhenGeneric: false));
            Assert.Equal(0, explicitCalls);

            var parser = TypeParser.GetTypeParser<List<TypeGrouped>>(columns);
            using var reader = Rows.Reader(columns, [1, "first", 10], [1, "changed", 11], [2, "second", 20]);
            reader.Read();
            var result = parser.Parse(reader).Result;

            Assert.True(wasAbsentDuringInitialization);
            Assert.Equal(2, result.Count);
            Assert.Equal((1, "first"), (result[0].Id, result[0].Label));
            Assert.Equal([10, 11], result[0].Values);
            Assert.Equal((2, "second"), (result[1].Id, result[1].Label));
            Assert.Equal([20], result[1].Values);
        }
        finally {
            TypeParsingInfo.RegistrationInitializer = previous;
            TypeParsingInfo.TryRemove(type, out _);
            TypeParsingInfo.TryRemove(explicitType, out _);
            TypeParser.Invalidate(columns, ParserInvalidationMode.InvalidateReferences);
        }
    }
}
