using Rinku.Mapping.Defaults;
using RinkuLib.Tests.Documentation;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Mapping;

/// <summary>Executable examples for docs/articles/customization/slot-rules.md.</summary>
[Collection("GlobalMappingConfiguration")]
public class AdvancedCustomizationDocumentationTests {
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class DbPrefixAttribute : Attribute, INameComparerMaker {
        public INameComparer MakeComparer(Type type, ref INameComparer current, object[] attributes, object? member)
            => new NameComparer("db_" + current.GetDefaultName());
    }

    private sealed record PrefixTrack([DbPrefix] int Id, string Name);
    private sealed record GlobalPrefixTrack(int Id, string Name);

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class NullAsDefaultAttribute : Attribute, INullColHandlerMaker {
        public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? member)
            => NullableTypeHandle.Instance;
    }

    private sealed record Stock([NullAsDefault] int Count);

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class ReusableSequentialAttribute : Attribute, IUsageFlagModifier {
        public void UpdateFlags(object? member, ref UsageFlags flags)
            => flags |= UsageFlags.CanReuse | UsageFlags.SequentialRead;
    }

    private sealed record ReusablePair([NoName, ReusableSequential] int First, [NoName] int Second);

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class PositionalReusableAttribute : Attribute, IParamInfoMaker {
        public ParamInfo MakeMatcher(Type type, INullColHandler nulls, INameComparer names, string? name,
            object[] attributes, UsageFlags flags, object? member)
            => new ParamInfoPlus(type, nulls, NoNameComparer.Instance,
                FlagUpdater.CanReuseAndSequential, IFallbackParserGetter.Nothing);
    }

    private sealed record MakerPair([PositionalReusable] int First, [NoName] int Second);

    [Fact]
    [DocumentationExample("slot-rules.md", "name-attribute")]
    public void A_name_maker_adds_a_second_column_name() {
        var result = Rows.ParseOne<PrefixTrack>(
            [new("db_Id", typeof(int), false), new("Name", typeof(string), false)],
            7, "Seven");

        Assert.Equal(new PrefixTrack(7, "Seven"), result);
    }

    [Fact]
    [DocumentationExample("slot-rules.md", "name-factory")]
    public void The_global_name_factory_changes_slots_created_while_it_is_installed() {
        var previous = ParamInfo.ComparerFactory;
        try {
            ParamInfo.ComparerFactory = (type, name, altNames, attributes, member, makers) => {
                var comparer = previous(type, name, altNames, attributes, member, makers);
                return name is null ? comparer : comparer.AddAltName("db_" + name);
            };

            var result = Rows.ParseOne<GlobalPrefixTrack>(
                [new("db_Id", typeof(int), false), new("db_Name", typeof(string), false)],
                8, "Eight");

            Assert.Equal(new GlobalPrefixTrack(8, "Eight"), result);
        }
        finally {
            ParamInfo.ComparerFactory = previous;
        }
    }

    [Fact]
    [DocumentationExample("slot-rules.md", "null-attribute")]
    public void A_null_handler_maker_can_return_the_type_default() {
        var result = Rows.ParseOne<Stock>([new("Count", typeof(int), true)], DBNull.Value);

        Assert.Equal(new Stock(0), result);
    }

    [Fact]
    [DocumentationExample("slot-rules.md", "usage-attribute")]
    public void A_usage_flag_attribute_can_read_one_column_twice() {
        var result = Rows.ParseOne<ReusablePair>([new("Anything", typeof(int), false)], 12);

        Assert.Equal(new ReusablePair(12, 12), result);
    }

    [Fact]
    [DocumentationExample("slot-rules.md", "full-param-info")]
    public void A_param_info_maker_can_replace_the_complete_slot_rule() {
        var result = Rows.ParseOne<MakerPair>([new("Anything", typeof(int), false)], 14);

        Assert.Equal(new MakerPair(14, 14), result);
    }
}
