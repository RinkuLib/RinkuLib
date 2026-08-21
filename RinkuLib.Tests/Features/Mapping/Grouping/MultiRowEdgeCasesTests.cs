using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;
using RinkuLib.Tests.Infrastructure;
using Rinku.Internal;
using Rinku.Mapping.Defaults;
using Rinku.Mapping.Parsers;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// Edge cases and outside-in extensibility of the multi-row road: custom group boundaries written entirely
/// against the public emit surface, method boundaries with several negotiated inputs and alternates, the
/// grouping extremes (one row, every row its own group, one group of every row), composite-key negatives, and
/// the collapse path. Every test checks the whole materialised graph.
/// </summary>
public class MultiRowEdgeCasesTests {

    static MultiRowEdgeCasesTests() {
        ((ICanUpdateGroupKey)TypeParsingInfo.ForceGet(typeof(StepHolder))).GroupKey = new StepMaker("Value", 10);
        ((ICanUpdateGroupKey)TypeParsingInfo.ForceGet(typeof(ParityHolder))).GroupKey = new ParityMaker("Seed");
        TypeParsingInfo.AddOrSet(typeof(Average), new MultiRowTypeParsingInfo(
            typeof(Averager).GetConstructor(Type.EmptyTypes)!,
            typeof(Averager).GetMethod(nameof(Averager.Add), [typeof(double)]),
            typeof(Averager).GetMethod(nameof(Averager.Finish))));
        TypeParsingInfo.AddOrSet(typeof(ExternalBucket<>), new ExternalMultiRowInfo(new MultiRowTypeParsingInfo(
            typeof(ExternalBucket<>).GetConstructor(Type.EmptyTypes)!,
            typeof(List<>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == nameof(List<int>.Add) && m.GetParameters().Length == 1),
            null)));
    }

    // --- aggregation: a custom accumulator folds rows into a value that is not a collection --------------

    public sealed class Averager {
        private double Sum;
        private int Count;
        public void Add(double value) {
            Sum += value;
            Count++;
        }
        public Average Finish() => new(Count == 0 ? 0 : Sum / Count, Count);
    }
    public readonly record struct Average(double Mean, int Count);

    public sealed class ExternalBucket<T> : List<T> { }

    [Fact]
    public void A_fold_requires_an_add_method_to_declare_its_element_type() {
        var error = Assert.Throws<RinkuConfigurationException>(() => new MultiRowTypeParsingInfo(
            typeof(List<>).GetConstructor(Type.EmptyTypes)!, null, null));

        Assert.Equal(ErrorCodes.TypeNotUsableByInfo, error.Code);
    }

    private sealed class ExternalMultiRowInfo(MultiRowTypeParsingInfo inner) : TypeParsingInfo, IMultiRowTypeParsingInfo {
        public override void ValidateCanUseType(Type targetType) => inner.ValidateCanUseType(targetType);
        public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo,
            ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default)
            => inner.TryGetParser(currentClosedType, previousUsages, paramInfo, columns, colModifier, ref colUsage, callerFlags);
    }

    private sealed class ExternalIntPlan(int column) : DbItemPlan, ISimpleDbItemPlan {
        public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
        public override bool IsSequencial(ref int previousIndex) {
            if (column <= previousIndex)
                return false;
            previousIndex = column;
            return true;
        }
        public void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldc_I4, column);
            generator.Emit(OpCodes.Callvirt, typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetInt32), [typeof(int)])!);
        }
    }

    private sealed class ExternalCompositeInfo(Type type) : TypeParsingInfo {
        private readonly ConstructorInfo constructor = type.GetConstructor([typeof(int)])!;
        public override void ValidateCanUseType(Type targetType) {
            if (targetType != type)
                throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo, $"the info handles only {type}");
        }
        public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo,
            ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default)
            => new CustomClassParser(currentClosedType, type, "value", NotNullHandle.Instance, constructor,
                [new ExternalIntPlan(0)]);
    }

    public sealed record ExternalCompositeValue(int Value) : IDbReadable;

    [Fact]
    public void An_external_simple_plan_can_be_nested_in_the_default_composite_plan() {
        TypeParsingInfo.AddOrSet(typeof(ExternalCompositeValue), new ExternalCompositeInfo(typeof(ExternalCompositeValue)));
        ColumnInfo[] cols = [new("Value", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<ExternalCompositeValue>(cols);
        using var reader = Rows.Reader(cols, [42]);
        reader.Read();

        Assert.Equal(new ExternalCompositeValue(42), parser.Parse(reader).Result);
    }

    public sealed record ExternalPlanParent([property: GroupKey] int Id, ExternalBucket<int> Values) : IDbReadable;

    [Fact]
    public void An_external_multi_row_info_uses_the_default_nested_handling() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Values", typeof(int), true),
        ];
        var parser = TypeParser.GetTypeParser<List<ExternalPlanParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 10],
            [1, 11],
            [1, DBNull.Value],
            [2, 20]);
        reader.Read();

        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal(2, result[1].Id);
        Assert.Equal([20], result[1].Values);
    }

    [Fact]
    [DocumentationExample("custom-multi-row-types.md", "aggregate-multi-row")]
    public void An_aggregate_folds_every_row_into_one_value() {
        ColumnInfo[] cols = [new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<Average>(cols);
        using var reader = Rows.Reader(cols, [10.0], [20.0], [30.0]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(new Average(20, 3), result);
    }

    [Fact]
    public void An_aggregate_of_no_rows_is_its_empty_fold() {
        ColumnInfo[] cols = [new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<Average>(cols);
        Assert.Equal(new Average(0, 0), parser.Default());
    }

    public sealed record Stats(int GroupId, Average Amount) : IDbReadable;

    [Fact]
    public void An_aggregate_folds_per_inferred_group() {
        ColumnInfo[] cols = [new("GroupId", typeof(int), false), new("Amount", typeof(double), false)];
        var parser = TypeParser.GetTypeParser<List<Stats>>(cols);
        using var reader = Rows.Reader(cols, [1, 10.0], [1, 20.0], [2, 30.0], [2, 50.0]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, new Average(15, 2)), (result[0].GroupId, result[0].Amount));
        Assert.Equal((2, new Average(40, 2)), (result[1].GroupId, result[1].Amount));
    }

    // --- outside-in extensibility: a boundary that reads the raw reader ----------------------------------

    public sealed record StepHolder(List<int> Value) : IDbReadable;

    /// <summary>A boundary defined outside the library that groups by <c>column / step</c>, read straight off the reader.</summary>
    private sealed class StepBoundary(int column, int step, IBoundaryField bucket) : GroupingBoundary {
        public override bool CanChange => true;
        public override bool Captures => true;
        private void EmitBucket(Generator g) {
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Ldc_I4, column);
            g.Emit(OpCodes.Callvirt, typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetInt32), [typeof(int)])!);
            g.Emit(OpCodes.Ldc_I4, step);
            g.Emit(OpCodes.Div);
        }
        public override void EmitCapture(Generator g) {
            bucket.EmitThis(g);
            EmitBucket(g);
            bucket.EmitStore(g);
        }
        public override void EmitCompare(Generator g, Label changed) {
            bucket.EmitLoad(g);
            EmitBucket(g);
            g.Emit(OpCodes.Ceq);
            g.Emit(OpCodes.Brfalse, changed);
        }
    }

    private sealed class StepMaker(string column, int step) : IGroupingRule {
        public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
            int index = Array.FindIndex(columns, c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
            return new StepBoundary(index, step, build.Field(typeof(int)));
        }
    }

    [Fact]
    public void A_boundary_defined_outside_the_library_groups_by_reading_the_reader() {
        ColumnInfo[] cols = [new("Value", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<StepHolder>>(cols);
        using var reader = Rows.Reader(cols, [3], [7], [12], [25], [28]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([3, 7], result[0].Value);
        Assert.Equal([12], result[1].Value);
        Assert.Equal([25, 28], result[2].Value);
    }

    // --- outside-in extensibility: a boundary that negotiates its own reader ------------------------------

    public sealed record ParityHolder(int Seed, List<string> Items) : IDbReadable;

    /// <summary>An outside boundary that groups while a negotiated column stays the same parity, using the build handles.</summary>
    private sealed class ParityBoundary(IBoundaryReader source, IBoundaryField parity) : GroupingBoundary {
        public override bool CanChange => true;
        public override bool Captures => true;
        private void EmitParity(Generator g) {
            source.EmitRead(g);
            g.Emit(OpCodes.Ldc_I4_2);
            g.Emit(OpCodes.Rem);
        }
        public override void EmitCapture(Generator g) {
            parity.EmitThis(g);
            EmitParity(g);
            parity.EmitStore(g);
        }
        public override void EmitCompare(Generator g, Label changed) {
            parity.EmitLoad(g);
            EmitParity(g);
            g.Emit(OpCodes.Ceq);
            g.Emit(OpCodes.Brfalse, changed);
        }
    }

    private sealed class ParityMaker(string column) : IGroupingRule {
        public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
            var plan = GroupKeyNegotiation.NegotiateReader(ParamInfo.Create(typeof(int), column, []).NameComparer, typeof(int), columns, colModifier, column);
            return new ParityBoundary(build.Reader(plan, typeof(int)), build.Field(typeof(int)));
        }
    }

    [Fact]
    public void A_boundary_defined_outside_the_library_negotiates_its_own_reader() {
        ColumnInfo[] cols = [new("Seed", typeof(int), false), new("Items", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<List<ParityHolder>>(cols);
        using var reader = Rows.Reader(cols, [2, "a"], [4, "b"], [5, "c"], [7, "d"], [8, "e"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Seed);
        Assert.Equal(["a", "b"], result[0].Items);
        Assert.Equal(5, result[1].Seed);
        Assert.Equal(["c", "d"], result[1].Items);
        Assert.Equal(8, result[2].Seed);
        Assert.Equal(["e"], result[2].Items);
    }

    // --- outside-in extensibility: a rule AND its attribute defined outside the library -------------------

    /// <summary>A rule written outside the library that groups by any column of a hand-coded name, whatever its type.</summary>
    private sealed class LooseColumnRule(string column) : IGroupingRule {
        public GroupingBoundary MakeBoundary(Type spanningType, ColumnInfo[] columns, ColModifier colModifier, IBoundaryBuild build) {
            var col = Array.Find(columns, c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
            var reader = GroupKeyNegotiation.NegotiateReader(ParamInfo.Create(col.Type, column, []).NameComparer, col.Type, columns, colModifier, column);
            return new EqualityBoundary([(build.Reader(reader, col.Type), build.Field(col.Type))]);
        }
    }

    /// <summary>An attribute written outside the library that makes a <see cref="LooseColumnRule"/>, found by the scan with no core change.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    private sealed class LooseColumnKeyAttribute(string column) : Attribute, IGroupingRuleMaker {
        public bool Composes(ICustomAttributeProvider carrier) => false;
        public IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers) => new LooseColumnRule(column);
    }

    [LooseColumnKey("Region")]
    public sealed record RegionGroup(int Ordinal, string Region, List<int> Codes) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "custom-group-rule")]
    public void A_rule_and_attribute_defined_outside_the_library_drive_grouping() {
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RegionGroup>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [3, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "West"), (result[0].Ordinal, result[0].Region));
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal((3, "East"), (result[1].Ordinal, result[1].Region));
        Assert.Equal([20], result[1].Codes);
    }

    // --- runtime setters: a rule set on a type or a construction path ------------------------------------

    public sealed record RuntimeTypeKeyed(int Ordinal, string Region, List<int> Codes) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "runtime-group-key")]
    public void SetGroupKey_sets_a_type_level_rule_at_runtime() {
        TypeParsingInfoHelper.SetGroupKey<RuntimeTypeKeyed>(new LooseColumnRule("Region"));
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeTypeKeyed>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [3, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "West"), (result[0].Ordinal, result[0].Region));
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal((3, "East"), (result[1].Ordinal, result[1].Region));
        Assert.Equal([20], result[1].Codes);
    }

    public sealed record RuntimePathKeyed(int Ordinal, string Region, List<int> Codes) : IDbReadable;
    [GroupKeyColumns("Region")]
    public sealed record RuntimeConditionalPathKeyed(int Ordinal, string Region, List<int> Codes) : IDbReadable;
    [GroupKeyColumns("MissingTypeKey")]
    public sealed record RuntimeThreeRuleFallback(int Ordinal, string Region, List<int> Codes) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "runtime-path-group-key")]
    public void SetGroupKey_sets_a_construction_level_rule_at_runtime() {
        TypeParsingInfo.GetOrAdd<RuntimePathKeyed>()
            .GetConstruction(typeof(int), typeof(string), typeof(List<int>)).GroupKey = new LooseColumnRule("Region");
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimePathKeyed>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [3, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "West"), (result[0].Ordinal, result[0].Region));
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal((3, "East"), (result[1].Ordinal, result[1].Region));
        Assert.Equal([20], result[1].Codes);
    }

    [Fact]
    public void A_construction_rule_that_does_not_match_falls_through_to_the_type_rule() {
        TypeParsingInfo.GetOrAdd<RuntimeConditionalPathKeyed>()
            .GetConstruction(typeof(int), typeof(string), typeof(List<int>)).GroupKey = new EqualityGroupingRule("Missing");
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeConditionalPathKeyed>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [3, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal([20], result[1].Codes);
    }

    [Fact]
    public void Construction_and_type_rules_can_both_fall_through_to_inference() {
        TypeParsingInfo.GetOrAdd<RuntimeThreeRuleFallback>()
            .GetConstruction(typeof(int), typeof(string), typeof(List<int>)).GroupKey = new EqualityGroupingRule("MissingConstructionKey");
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeThreeRuleFallback>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [1, "West", 11], [2, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal([20], result[1].Codes);
    }

    [GroupKeyColumns("Region")]
    public sealed record RuntimeClearTypeKey(int Ordinal, string Region, List<int> Codes) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "runtime-group-key")]
    public void ClearGroupKey_restores_the_type_attribute_default() {
        TypeParsingInfoHelper.ClearGroupKey<RuntimeClearTypeKey>();
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeClearTypeKey>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [1, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "West"), (result[0].Ordinal, result[0].Region));
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal((1, "East"), (result[1].Ordinal, result[1].Region));
        Assert.Equal([20], result[1].Codes);
    }

    [GroupKeyColumns("Region")]
    public sealed record RuntimeClearPathKey([param: GroupKey] int Ordinal, string Region, List<int> Codes) : IDbReadable;

    [Fact]
    public void ClearGroupKey_on_a_path_removes_its_attribute_and_restores_the_type_rule() {
        var ctor = typeof(RuntimeClearPathKey).GetConstructors().Single();
        TypeParsingInfo.GetOrAdd<RuntimeClearPathKey>().GetConstruction(ctor).GroupKey = null;
        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeClearPathKey>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [1, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "West"), (result[0].Ordinal, result[0].Region));
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal((1, "East"), (result[1].Ordinal, result[1].Region));
        Assert.Equal([20], result[1].Codes);
    }

    public sealed record RuntimeExactPathKey(int Ordinal, string Region, List<int> Codes) : IDbReadable {
        public static RuntimeExactPathKey FromOrdinal(int ordinal, string region, List<int> codes) => new(ordinal, region, codes);
        public static RuntimeExactPathKey FromRegion(int ordinal, string region, List<int> codes) => new(ordinal, region, codes);
    }

    [Fact]
    public void SetGroupKey_can_target_the_exact_factory_when_paths_have_the_same_shape() {
        var factory = typeof(RuntimeExactPathKey).GetMethod(nameof(RuntimeExactPathKey.FromRegion))!;
        var rule = new LooseColumnRule("Region");
        TypeParsingInfo.GetOrAdd<RuntimeExactPathKey>().GetConstruction(factory).GroupKey = rule;

        var info = (ICanProvideConstructions)TypeParsingInfo.ForceGet(typeof(RuntimeExactPathKey));
        var constructions = info.PossibleConstructors.ToArray();
        var selected = constructions.Single(c => c.MethodBase == factory);
        Assert.Same(rule, selected.GroupKey);
        Assert.All(constructions.Where(c => c.MethodBase != factory), c => Assert.Null(c.GroupKey));
    }

    public sealed record RuntimeMethodPathKey(int Ordinal, string Region, List<int> Codes) : IDbReadable {
        public static (bool Same, string Next) ByRegion(string stored, string region) => (stored == region, region);
    }

    [Fact]
    public void A_construction_path_can_hold_a_runtime_method_grouping_rule() {
        var ruleMethod = typeof(RuntimeMethodPathKey).GetMethod(nameof(RuntimeMethodPathKey.ByRegion))!;
        TypeParsingInfo.GetOrAdd<RuntimeMethodPathKey>()
            .GetConstruction(typeof(int), typeof(string), typeof(List<int>)).GroupKey = new MethodGroupingRule(ruleMethod);
        var info = (ICanProvideConstructions)TypeParsingInfo.ForceGet(typeof(RuntimeMethodPathKey));
        Assert.IsType<MethodGroupingRule>(info.PossibleConstructors.ToArray().Single().GroupKey);
    }

    public sealed record RuntimeTakeover(int Ordinal, string Region, List<int> Codes) : IDbReadable;

    private sealed class TakeoverTypeInfo(Type target) : TypeParsingInfo, ICanUpdateGroupKey {
        private readonly DefaultTypeParsingInfo inner = new(target);
        public IGroupingRule? GroupKey { get; set; }
        public override void ValidateCanUseType(Type targetType) => inner.ValidateCanUseType(targetType);
        public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo,
            ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage, MethodCtorInfo.AdditionalFlags callerFlags = default) {
            inner.GroupKey = GroupKey;
            return inner.TryGetParser(currentClosedType, previousUsages, paramInfo, columns, colModifier, ref colUsage, callerFlags);
        }
    }

    [Fact]
    public void A_custom_type_info_can_take_over_grouping_through_the_public_runtime_capability() {
        var info = new TakeoverTypeInfo(typeof(RuntimeTakeover));
        TypeParsingInfo.AddOrSet(typeof(RuntimeTakeover), info);
        Assert.True(info.SetGroupKey(new LooseColumnRule("Region")));

        ColumnInfo[] cols = [new("Ordinal", typeof(int), false), new("Region", typeof(string), false), new("Codes", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeTakeover>>(cols);
        using var reader = Rows.Reader(cols, [1, "West", 10], [2, "West", 11], [3, "East", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Codes);
        Assert.Equal([20], result[1].Codes);
    }

    // --- outside-in extensibility: a null-element rule defined outside the library -----------------------

    /// <summary>A null rule written outside the library that keeps a null element as the type's default instead of skipping it.</summary>
    private sealed class DefaultWhenNullHandle : INullColHandler {
        public static readonly DefaultWhenNullHandle Instance = new();
        public bool NeedNullJumpSetPoint(Type closedType) => false;
        public bool IsBr_S(Type closedType) => true;
        public Label? HandleNull(Type parentType, Type closedType, string paramName, Generator generator, NullSetPoint nullSetPoint) {
            var end = generator.DefineLabel();
            DbItemPlan.EmitDefaultValue(closedType, generator);
            generator.Emit(OpCodes.Br_S, end);
            return end;
        }
        public Label? HandleNullForMultiRow(Type bufferType, Type elementType, string paramName, System.Reflection.Emit.LocalBuilder elementLocal, Generator generator, NullSetPoint nullSetPoint) {
            DbItemPlan.EmitDefaultValue(elementType, generator);
            generator.Emit(OpCodes.Stloc, elementLocal);
            return null;
        }
        public INullColHandler SetAbortOnNull(Type type, bool abortOnNull) => this;
    }

    /// <summary>An attribute written outside the library that puts <see cref="DefaultWhenNullHandle"/> on a collection member, found by the same seam as the built-in rules with no core change.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
    private sealed class DefaultWhenNullElementAttribute : Attribute, INullColHandlerMaker {
        public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param) => DefaultWhenNullHandle.Instance;
    }

    public sealed record ScoreCard(int Player, [DefaultWhenNullElement] List<int> Scores) : IDbReadable;

    [Fact]
    public void A_null_element_rule_defined_outside_the_library_keeps_a_default() {
        ColumnInfo[] cols = [new("Player", typeof(int), false), new("Scores", typeof(int), true)];
        var parser = TypeParser.GetTypeParser<List<ScoreCard>>(cols);
        using var reader = Rows.Reader(cols, [1, 10], [1, DBNull.Value], [1, 30]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([10, 0, 30], result[0].Scores);
    }

    // --- method boundary edge cases ----------------------------------------------------------------------

    public sealed record TwoKeyChild([AbortOnNull] int Id, string Value) : IDbReadable;
    public sealed record ManhattanBucket(List<TwoKeyChild> Children) : IDbReadable {
        [GroupKey]
        public static (bool Same, (int, int) Next) SameCell((int X, int Y) stored, int gx, int gy)
            => ((gx, gy) == stored, (gx, gy));
    }

    [Fact]
    public void A_method_boundary_reads_several_negotiated_columns() {
        ColumnInfo[] cols = [
            new("Gx", typeof(int), false),
            new("Gy", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<ManhattanBucket>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 1, 10, "a"],
            [1, 1, 11, "b"],
            [1, 2, 12, "c"],
            [2, 2, 13, "d"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new TwoKeyChild(10, "a"), new TwoKeyChild(11, "b")], result[0].Children);
        Assert.Equal([new TwoKeyChild(12, "c")], result[1].Children);
        Assert.Equal([new TwoKeyChild(13, "d")], result[2].Children);
    }

    public sealed record AltMethodBucket(List<TwoKeyChild> Children) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) SameKey(int stored, [Alt("Grp")] int bucket) => (bucket == stored, bucket);
    }

    [Fact]
    public void A_method_boundary_parameter_resolves_through_its_alternate_name() {
        ColumnInfo[] cols = [
            new("Grp", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<AltMethodBucket>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, "a"], [1, 11, "b"], [2, 12, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new TwoKeyChild(10, "a"), new TwoKeyChild(11, "b")], result[0].Children);
        Assert.Equal([new TwoKeyChild(12, "c")], result[1].Children);
    }

    public sealed record MGrand([AbortOnNull] int Id, string Data) : IDbReadable;
    public sealed record MethodChild(List<MGrand> Grands) : IDbReadable {
        [GroupKey]
        public static (bool Same, int Next) SameChild(int stored, int childKey) => (childKey == stored, childKey);
    }
    public sealed record MethodNestParent([property: GroupKey] int Id, string Name, List<MethodChild> Children) : IDbReadable;

    [Fact]
    public void A_method_keyed_sub_level_folds_under_an_equality_keyed_parent() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenChildKey", typeof(int), false),
            new("ChildrenGrandsId", typeof(int), true),
            new("ChildrenGrandsData", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MethodNestParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, 100, "g100"],
            [1, "P1", 10, 101, "g101"],
            [1, "P1", 11, 110, "g110"],
            [2, "P2", 20, 200, "g200"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal([new MGrand(100, "g100"), new MGrand(101, "g101")], result[0].Children[0].Grands);
        Assert.Equal([new MGrand(110, "g110")], result[0].Children[1].Grands);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Single(result[1].Children);
        Assert.Equal([new MGrand(200, "g200")], result[1].Children[0].Grands);
    }

    [Fact]
    public void A_method_keyed_sub_level_stays_empty_for_a_childless_parent() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenChildKey", typeof(int), true),
            new("ChildrenGrandsId", typeof(int), true),
            new("ChildrenGrandsData", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MethodNestParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, 100, "g100"],
            [2, "P2", DBNull.Value, DBNull.Value, DBNull.Value],
            [3, "P3", 30, 300, "g300"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new MGrand(100, "g100")], result[0].Children[0].Grands);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Empty(result[1].Children);
        Assert.Equal([new MGrand(300, "g300")], result[2].Children[0].Grands);
    }

    // --- grouping extremes -------------------------------------------------------------------------------

    private static ColumnInfo[] ParentCols() => [
        new("Id", typeof(int), false),
        new("Name", typeof(string), false),
        new("ChildrenId", typeof(int), true),
        new("ChildrenValue", typeof(string), true),
    ];

    [Fact]
    public void A_single_row_yields_one_group_of_one_child() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
    }

    [Fact]
    public void A_key_that_changes_every_row_yields_a_singleton_group_each() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [2, "P2", 20, "c20"], [3, "P3", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
        Assert.Equal((3, "P3"), (result[2].Id, result[2].Name));
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
    }

    [Fact]
    public void A_key_that_never_changes_folds_every_row_into_one_group() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [1, "P1", 12, "c12"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11"), new MultiRowTests.Child(12, "c12")], result[0].Children);
    }

    [Fact]
    public void A_childless_parent_from_a_left_join_keeps_an_empty_collection() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.Parent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "P1", 10, "c10"],
            [2, "P2", DBNull.Value, DBNull.Value],
            [3, "P3", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([new MultiRowTests.Child(10, "c10")], result[0].Children);
        Assert.Equal((2, "P2"), (result[1].Id, result[1].Name));
        Assert.Empty(result[1].Children);
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
    }

    // --- composite key: both parts must match ------------------------------------------------------------

    [Fact]
    public void A_composite_key_splits_when_only_the_second_part_changes() {
        ColumnInfo[] cols = [
            new("RegionId", typeof(int), false),
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MultiRowTests.RegionParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 1, "A", 10, "c10"],
            [1, 1, "A", 11, "c11"],
            [1, 2, "B", 20, "c20"],
            [2, 2, "C", 30, "c30"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal((1, 1, "A"), (result[0].RegionId, result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal((1, 2, "B"), (result[1].RegionId, result[1].Id, result[1].Name));
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
        Assert.Equal((2, 2, "C"), (result[2].RegionId, result[2].Id, result[2].Name));
        Assert.Equal([new MultiRowTests.Child(30, "c30")], result[2].Children);
    }

    // --- a key resolves the throw and can sit anywhere ---------------------------------------------------

    public sealed record Ledger(List<int> Lines, [GroupKey] int AccountId) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "parameter-group-key")]
    public void A_parameter_key_after_the_collection_resolves_the_throw_and_groups() {
        ColumnInfo[] cols = [new("Lines", typeof(int), false), new("AccountId", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Ledger>>(cols);
        using var reader = Rows.Reader(cols, [10, 1], [11, 1], [20, 2]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].AccountId);
        Assert.Equal([10, 11], result[0].Lines);
        Assert.Equal(2, result[1].AccountId);
        Assert.Equal([20], result[1].Lines);
    }

    private static ColumnInfo[] ArtistAlbumCols() => [
        new("Id", typeof(int), false), new("Name", typeof(string), false),
        new("AlbumsId", typeof(int), true), new("AlbumsTitle", typeof(string), true)];

    public sealed record PlainAlbum(int Id, string Title) : IDbReadable;
    public sealed record PlainArtist([property: GroupKey] int Id, string Name, List<PlainAlbum> Albums) : IDbReadable;

    [Fact]
    public void A_null_in_a_required_element_column_throws() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<PlainArtist>>(cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        Refusals.Raises(ErrorCodes.NullNotAllowed, () => parser.Parse(reader));
    }

    public sealed record CollapsingAlbum([AbortOnNull] int Id, string Title) : IDbReadable;
    public sealed record CollapsingArtist([property: GroupKey] int Id, string Name, List<CollapsingAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_element_that_collapses_on_null_is_skipped() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<CollapsingArtist>>(cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Empty(result[0].Albums);
    }

    public sealed record NullableAlbum(int? Id, string? Title) : IDbReadable;
    public sealed record NullableArtist([property: GroupKey] int Id, string Name, List<NullableAlbum?> Albums) : IDbReadable;

    [Fact]
    public void An_all_null_element_that_still_builds_is_kept() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<NullableArtist>>(cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([new NullableAlbum(null, null)], result[0].Albums);
    }

    public sealed record KeptArtist([property: GroupKey] int Id, string Name, List<NullableAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_all_null_object_element_is_kept_without_the_attribute() {
        var cols = ArtistAlbumCols();
        var parser = TypeParser.GetTypeParser<List<KeptArtist>>(cols);
        using var reader = Rows.Reader(cols, [3, "Bjork", DBNull.Value, DBNull.Value]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Single(result);
        Assert.Equal([new NullableAlbum(null, null)], result[0].Albums);
    }

    public sealed class ThrowOnNullElementAttribute : Attribute, INullColHandlerMaker {
        public INullColHandler MakeColHandler(Type type, string? name, object[] attributes, object? param) => NotNullHandle.Instance;
    }
    public sealed record StrictTags(int Id, [ThrowOnNullElement] List<string?> Tags) : IDbReadable;

    [Fact]
    public void A_custom_element_rule_can_throw_on_a_null_element() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Tags", typeof(string), true)];
        var parser = TypeParser.GetTypeParser<List<StrictTags>>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [1, DBNull.Value], [1, "b"]);
        reader.Read();
        Refusals.Raises(ErrorCodes.NullNotAllowed, () => parser.Parse(reader));
    }

    [GroupKeyColumns("Key")]
    public sealed record KeyedWidget(string Label, List<int> Values) : IDbReadable;

    [Fact]
    public void A_type_key_that_maps_no_column_falls_through_to_inference() {
        ColumnInfo[] cols = [new("Label", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<KeyedWidget>>(cols);
        using var reader = Rows.Reader(cols, ["A", 10], ["A", 11], ["B", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal([20], result[1].Values);
    }

    [Fact]
    public void An_empty_equality_group_key_is_rejected_as_configuration() {
        var error = Assert.Throws<RinkuConfigurationException>(() => new EqualityGroupingRule(Array.Empty<MemberInfo>()));

        Assert.Equal(ErrorCodes.GroupKeyUnmapped, error.Code);
    }

    public sealed record RuntimeKeyed(int Id, string Name, List<int> Values) : IDbReadable;

    [Fact]
    public void Setting_a_group_key_at_runtime_narrows_the_boundary() {
        TypeParsingInfoHelper.SetGroupKey<RuntimeKeyed>(nameof(RuntimeKeyed.Id));
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<RuntimeKeyed>>(cols);
        using var reader = Rows.Reader(cols, [1, "a", 10], [1, "b", 11], [2, "c", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "a"), (result[0].Id, result[0].Name));
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal((2, "c"), (result[1].Id, result[1].Name));
        Assert.Equal([20], result[1].Values);
    }

    // --- a tuple is not special: a named type folds the same by default ----------------------------------

    public sealed record Pair(List<int> Numbers, List<string> Words) : IDbReadable;

    [Fact]
    public void A_named_type_with_only_collections_folds_the_whole_result() {
        ColumnInfo[] cols = [new("Numbers", typeof(int), false), new("Words", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<Pair>(cols);
        using var reader = Rows.Reader(cols, [1, "a"], [2, "b"], [3, "c"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal([1, 2, 3], result.Numbers);
        Assert.Equal(["a", "b", "c"], result.Words);
    }

    public sealed record Regional(int Region, List<decimal> Amounts) : IDbReadable;

    [Fact]
    public void Querying_a_single_spanning_value_stops_at_the_boundary_change() {
        ColumnInfo[] cols = [new("Region", typeof(int), false), new("Amounts", typeof(decimal), false)];
        var parser = TypeParser.GetTypeParser<Regional>(cols);
        using var reader = Rows.Reader(cols, [1, 9.99m], [1, 4.00m], [2, 5.00m]);
        reader.Read();
        var (canContinue, first) = parser.Parse(reader);

        Assert.True(canContinue);
        Assert.Equal(1, first.Region);
        Assert.Equal([9.99m, 4.00m], first.Amounts);
    }

    [Fact]
    public void A_named_type_with_a_leading_value_keys_on_it() {
        ColumnInfo[] cols = [new("Region", typeof(int), false), new("Amounts", typeof(decimal), false)];
        var parser = TypeParser.GetTypeParser<List<Regional>>(cols);
        using var reader = Rows.Reader(cols, [1, 9.99m], [1, 4.00m], [2, 5.00m]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Region);
        Assert.Equal([9.99m, 4.00m], result[0].Amounts);
        Assert.Equal(2, result[1].Region);
        Assert.Equal([5.00m], result[1].Amounts);
    }

    // --- values around a collection: before, after, between, one or many ---------------------------------

    public sealed record TwoBefore(int A, int B, List<int> Items) : IDbReadable;

    [Fact]
    public void Several_values_before_the_collection_all_key() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("B", typeof(int), false), new("Items", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TwoBefore>>(cols);
        using var reader = Rows.Reader(cols, [1, 1, 10], [1, 1, 11], [1, 2, 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 1), (result[0].A, result[0].B));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal((1, 2), (result[1].A, result[1].B));
        Assert.Equal([20], result[1].Items);
    }

    public sealed record TwoAfter(int A, List<int> Items, int X, int Y) : IDbReadable;

    [Fact]
    public void Several_values_after_the_collection_are_captured_once() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("Items", typeof(int), false), new("X", typeof(int), false), new("Y", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TwoAfter>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, 7, 8], [1, 11, 7, 8], [2, 20, 5, 6]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 7, 8), (result[0].A, result[0].X, result[0].Y));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal((2, 5, 6), (result[1].A, result[1].X, result[1].Y));
        Assert.Equal([20], result[1].Items);
    }

    public sealed record Between(int A, List<int> Items, int Mid, List<int> Others) : IDbReadable;

    [Fact]
    public void Values_between_two_collections_are_captured_once() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("Items", typeof(int), false), new("Mid", typeof(int), false), new("Others", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Between>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, 99, 20], [1, 11, 99, 21], [2, 12, 88, 22]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 99), (result[0].A, result[0].Mid));
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal([20, 21], result[0].Others);
        Assert.Equal((2, 88), (result[1].A, result[1].Mid));
        Assert.Equal([12], result[1].Items);
        Assert.Equal([22], result[1].Others);
    }

    public sealed record Report(List<int> Rows, int Total) : IDbReadable;

    [Fact]
    public void A_value_after_a_collection_with_none_before_throws() {
        ColumnInfo[] cols = [new("Rows", typeof(int), false), new("Total", typeof(int), false)];
        Refusals.Raises(ErrorCodes.MissingGroupBoundary,
            () => TypeParser.GetTypeParser<Report>(cols));
    }

    // --- an alt on the collection changes the element prefix ---------------------------------------------

    public sealed record AltAlbum(int Id, string Title) : IDbReadable;
    public sealed record AltArtist(int Id, string Name, [Alt("Album")] List<AltAlbum> Albums) : IDbReadable;

    [Fact]
    public void An_alt_on_a_collection_lets_its_element_columns_drop_the_plural() {
        ColumnInfo[] cols = [
            new("Id", typeof(int), false),
            new("Name", typeof(string), false),
            new("AlbumId", typeof(int), true),
            new("AlbumTitle", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<AltArtist>>(cols);
        using var reader = Rows.Reader(cols,
            [1, "AC/DC", 10, "High Voltage"],
            [1, "AC/DC", 11, "Let There Be Rock"],
            [2, "Queen", 20, "Jazz"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new AltAlbum(10, "High Voltage"), new AltAlbum(11, "Let There Be Rock")], result[0].Albums);
        Assert.Equal([new AltAlbum(20, "Jazz")], result[1].Albums);
    }

    // --- keys on construction parameters -----------------------------------------------------------------

    public sealed record ParamKeyParent([GroupKey] int Id, string Name, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void A_construction_parameter_marked_as_key_groups_by_its_column() {
        var cols = ParentCols();
        var parser = TypeParser.GetTypeParser<List<ParamKeyParent>>(cols);
        using var reader = Rows.Reader(cols, [1, "P1", 10, "c10"], [1, "P1", 11, "c11"], [2, "P2", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, "P1"), (result[0].Id, result[0].Name));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed record OverrideParent([GroupKey] int CtorKey, [property: GroupKey] int TypeKey, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void A_construction_parameter_key_has_priority_over_the_type_level_member_key() {
        ColumnInfo[] cols = [
            new("CtorKey", typeof(int), false),
            new("TypeKey", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<OverrideParent>>(cols);
        using var reader = Rows.Reader(cols,
            [1, 100, 10, "c10"],
            [1, 200, 11, "c11"],
            [2, 100, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].CtorKey);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal(2, result[1].CtorKey);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed record CompositeParamParent([GroupKey] int A, [GroupKey] int B, List<MultiRowTests.Child> Children) : IDbReadable;

    [Fact]
    public void Composite_construction_parameter_keys_compose() {
        ColumnInfo[] cols = [
            new("A", typeof(int), false),
            new("B", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<CompositeParamParent>>(cols);
        using var reader = Rows.Reader(cols, [1, 1, 10, "c10"], [1, 1, 11, "c11"], [1, 2, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal((1, 1), (result[0].A, result[0].B));
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal((1, 2), (result[1].A, result[1].B));
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    // --- a member key that diverges from the default shape ------------------------------------------------

    public sealed record MemberKeyShape(int Leading, [property: GroupKey] int Actual, List<MultiRowTests.Child> Children) : IDbReadable;

    public sealed class SoftMemberKey : IDbReadable {
        [GroupKey, Alt("Key")]
        public int Id { get; }
        public string? Code { get; }
        public List<MultiRowTests.Child> Children { get; }
        public SoftMemberKey(int key, List<MultiRowTests.Child> children) {
            Id = key;
            Children = children;
        }
        public SoftMemberKey(string code, List<MultiRowTests.Child> children) {
            Code = code;
            Children = children;
        }
    }

    [Fact]
    public void A_member_key_groups_by_its_column_not_the_leading_shape() {
        ColumnInfo[] cols = [
            new("Leading", typeof(int), false),
            new("Actual", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<MemberKeyShape>>(cols);
        using var reader = Rows.Reader(cols, [100, 1, 10, "c10"], [200, 1, 11, "c11"], [300, 2, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Actual);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal(2, result[1].Actual);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    [Fact]
    [DocumentationExample("grouping.md", "soft-member-group-key")]
    public void A_type_member_key_applies_when_its_name_comparer_matches_the_schema() {
        ColumnInfo[] cols = [
            new("Key", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<SoftMemberKey>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, "c10"], [1, 11, "c11"], [2, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Children.Select(child => child.Id));
        Assert.Equal([20], result[1].Children.Select(child => child.Id));
    }

    [Fact]
    public void A_type_member_key_yields_to_inference_when_the_schema_has_no_matching_column() {
        ColumnInfo[] cols = [
            new("Code", typeof(string), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<SoftMemberKey>>(cols);
        using var reader = Rows.Reader(cols, ["A", 10, "c10"], ["A", 11, "c11"], ["B", 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Code);
        Assert.Equal([10, 11], result[0].Children.Select(child => child.Id));
        Assert.Equal("B", result[1].Code);
        Assert.Equal([20], result[1].Children.Select(child => child.Id));
    }

    // --- construction method priority and same-level conflicts ---------------------------------------------

    public sealed class CtorMethodKeyed : IDbReadable {
        [GroupKeyMethod(nameof(SameTens))]
        public CtorMethodKeyed(int value, List<MultiRowTests.Child> children) {
            Value = value;
            Children = children;
        }
        public int Value { get; }
        public List<MultiRowTests.Child> Children { get; }
        public static (bool Same, int Next) SameTens(int stored, int value) => (value / 10 == stored / 10, value);
    }

    [Fact]
    public void A_construction_method_reference_has_priority_over_the_default_shape() {
        ColumnInfo[] cols = [
            new("Value", typeof(int), false),
            new("ChildrenId", typeof(int), true),
            new("ChildrenValue", typeof(string), true),
        ];
        var parser = TypeParser.GetTypeParser<List<CtorMethodKeyed>>(cols);
        using var reader = Rows.Reader(cols, [1, 10, "c10"], [3, 11, "c11"], [25, 20, "c20"]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([new MultiRowTests.Child(10, "c10"), new MultiRowTests.Child(11, "c11")], result[0].Children);
        Assert.Equal([new MultiRowTests.Child(20, "c20")], result[1].Children);
    }

    public sealed class CtorKeyConflict : IDbReadable {
        [GroupKeyMethod(nameof(Same))]
        public CtorKeyConflict([GroupKey] int a, List<MultiRowTests.Child> children) {
            A = a;
            Children = children;
        }
        public int A { get; }
        public List<MultiRowTests.Child> Children { get; }
        public static (bool Same, int Next) Same(int stored, int a) => (a == stored, a);
    }

    [Fact]
    public void A_construction_with_both_a_method_reference_and_a_key_parameter_throws() {
        ColumnInfo[] cols = [new("A", typeof(int), false), new("ChildrenId", typeof(int), true), new("ChildrenValue", typeof(string), true)];
        Refusals.Raises(ErrorCodes.ConflictingGroupKey, () => TypeParser.GetTypeParser<List<CtorKeyConflict>>(cols));
    }

    public sealed class TypeKeyConflict : IDbReadable {
        [GroupKey] public int Id { get; set; }
        public List<MultiRowTests.Child> Children { get; set; } = [];
        [GroupKey] public static (bool Same, int Next) Key(int stored, int id) => (id == stored, id);
    }

    [Fact]
    public void A_member_key_and_a_method_key_on_one_type_throw() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("ChildrenId", typeof(int), true), new("ChildrenValue", typeof(string), true)];
        Refusals.Raises(ErrorCodes.ConflictingGroupKey, () => TypeParser.GetTypeParser<List<TypeKeyConflict>>(cols));
    }

    // --- a group key on a member that is not a constructor parameter --------------------------------------

    public sealed class Timeline : IDbReadable {
        [CanCompleteWithMembers]
        public Timeline(string label, List<int> marks) {
            Label = label;
            Marks = marks;
        }
        public string Label { get; }
        public List<int> Marks { get; }
        [GroupKey] public int Track { get; set; }
    }

    [Fact]
    public void A_group_key_on_a_member_outside_the_constructor_matches_the_schema() {
        ColumnInfo[] cols = [new("Track", typeof(int), false), new("Label", typeof(string), false), new("Marks", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Timeline>>(cols);
        using var reader = Rows.Reader(cols, [1, "morning", 10], [1, "evening", 11], [2, "morning", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Track);
        Assert.Equal("morning", result[0].Label);
        Assert.Equal([10, 11], result[0].Marks);
        Assert.Equal(2, result[1].Track);
        Assert.Equal("morning", result[1].Label);
        Assert.Equal([20], result[1].Marks);
    }

    public sealed class MemberKeyAccount : IDbReadable {
        public MemberKeyAccount(string holder, List<int> entries) {
            Holder = holder;
            Entries = entries;
        }
        public string Holder { get; }
        public List<int> Entries { get; }
        [GroupKey] public int Number { get; }
    }

    [Fact]
    public void A_get_only_member_key_outside_the_constructor_matches_the_schema() {
        ColumnInfo[] cols = [new("Number", typeof(int), false), new("Holder", typeof(string), false), new("Entries", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<MemberKeyAccount>>(cols);
        using var reader = Rows.Reader(cols, [1, "Ada", 10], [1, "Ada", 11], [2, "Bo", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("Ada", result[0].Holder);
        Assert.Equal([10, 11], result[0].Entries);
        Assert.Equal("Bo", result[1].Holder);
        Assert.Equal([20], result[1].Entries);
        Assert.Equal(0, result[0].Number);
    }

    [GroupKeyColumns("Number")]
    public sealed record ColumnKeyAccount(string Holder, List<int> Entries) : IDbReadable;

    [Fact]
    [DocumentationExample("grouping.md", "column-group-key")]
    public void A_column_name_key_on_the_type_groups_with_no_member() {
        ColumnInfo[] cols = [new("Number", typeof(int), false), new("Holder", typeof(string), false), new("Entries", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<ColumnKeyAccount>>(cols);
        using var reader = Rows.Reader(cols, [1, "Ada", 10], [1, "Ada", 11], [2, "Bo", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("Ada", result[0].Holder);
        Assert.Equal([10, 11], result[0].Entries);
        Assert.Equal("Bo", result[1].Holder);
        Assert.Equal([20], result[1].Entries);
    }

    // --- the negotiated construction path decides the grouping --------------------------------------------

    public sealed class Route : IDbReadable {
        public Route(int Line, List<int> stops) {
            Key = Line;
            Stops = stops;
        }
        public Route(int From, int To, List<int> stops) {
            Key = From * 1000 + To;
            Stops = stops;
        }
        public int Key { get; }
        public List<int> Stops { get; }
    }

    [Fact]
    public void The_chosen_construction_path_decides_the_grouping() {
        ColumnInfo[] byLine = [new("Line", typeof(int), false), new("Stops", typeof(int), false)];
        var a = TypeParser.GetTypeParser<List<Route>>(byLine);
        using var ra = Rows.Reader(byLine, [1, 10], [1, 11], [2, 20]);
        ra.Read();
        var resA = a.Parse(ra).Result;
        Assert.Equal(2, resA.Count);
        Assert.Equal([10, 11], resA[0].Stops);
        Assert.Equal([20], resA[1].Stops);

        ColumnInfo[] byPair = [new("From", typeof(int), false), new("To", typeof(int), false), new("Stops", typeof(int), false)];
        var b = TypeParser.GetTypeParser<List<Route>>(byPair);
        using var rb = Rows.Reader(byPair, [1, 5, 10], [1, 5, 11], [1, 6, 20]);
        rb.Read();
        var resB = b.Parse(rb).Result;
        Assert.Equal(2, resB.Count);
        Assert.Equal([10, 11], resB[0].Stops);
        Assert.Equal([20], resB[1].Stops);
    }

    // --- a group key by column name, no member needed ----------------------------------------------------

    public sealed record Ticker(List<int> Prices) : IDbReadable;

    [Fact]
    public void A_group_key_by_column_name_groups_by_any_column_type() {
        TypeParsingInfoHelper.SetGroupKeyColumns<Ticker>("Symbol");

        ColumnInfo[] ints = [new("Symbol", typeof(int), false), new("Prices", typeof(int), false)];
        var pInt = TypeParser.GetTypeParser<List<Ticker>>(ints);
        using var rInt = Rows.Reader(ints, [1, 100], [1, 101], [2, 200]);
        rInt.Read();
        var byInt = pInt.Parse(rInt).Result;
        Assert.Equal(2, byInt.Count);
        Assert.Equal([100, 101], byInt[0].Prices);
        Assert.Equal([200], byInt[1].Prices);

        ColumnInfo[] strs = [new("Symbol", typeof(string), false), new("Prices", typeof(int), false)];
        var pStr = TypeParser.GetTypeParser<List<Ticker>>(strs);
        using var rStr = Rows.Reader(strs, ["AAA", 100], ["AAA", 101], ["BBB", 200]);
        rStr.Read();
        var byStr = pStr.Parse(rStr).Result;
        Assert.Equal(2, byStr.Count);
        Assert.Equal([100, 101], byStr[0].Prices);
        Assert.Equal([200], byStr[1].Prices);
    }

    public sealed record Box<T>([property: GroupKey] T Key, List<int> Values) : IDbReadable;

    [Fact]
    public void A_generic_member_key_resolves_at_negotiation() {
        ColumnInfo[] cols = [new("Key", typeof(int), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Box<int>>>(cols);
        using var reader = Rows.Reader(cols, [1, 100], [1, 101], [2, 200]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Key);
        Assert.Equal([100, 101], result[0].Values);
        Assert.Equal(2, result[1].Key);
        Assert.Equal([200], result[1].Values);
    }

    public sealed record Crate<T>([GroupKey] T Tag, List<int> Values) : IDbReadable;

    [Fact]
    public void A_generic_construction_parameter_key_resolves_at_negotiation() {
        ColumnInfo[] cols = [new("Tag", typeof(int), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<Crate<int>>>(cols);
        using var reader = Rows.Reader(cols, [1, 100], [1, 101], [2, 200]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Tag);
        Assert.Equal([100, 101], result[0].Values);
        Assert.Equal(2, result[1].Tag);
        Assert.Equal([200], result[1].Values);
    }

    public sealed class TypeMethodBoundary : IDbReadable {
        public TypeMethodBoundary(DateTime sessionDate, List<int> values) {
            SessionDate = sessionDate;
            Values = values;
        }
        public DateTime SessionDate { get; }
        public List<int> Values { get; }
        [GroupKey]
        public static (bool Same, DateTime Next) BySessionDate(DateTime previous, DateTime sessionDate)
            => (sessionDate == previous, sessionDate);
    }

    [Fact]
    [DocumentationExample("grouping.md", "method-group-key")]
    public void A_type_level_static_method_boundary_groups_by_the_method_logic() {
        ColumnInfo[] cols = [new("SessionDate", typeof(DateTime), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TypeMethodBoundary>>(cols);
        var d1 = new DateTime(2026, 7, 30);
        var d2 = new DateTime(2026, 7, 31);
        using var reader = Rows.Reader(cols, [d1, 10], [d1, 11], [d2, 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(d1, result[0].SessionDate);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal(d2, result[1].SessionDate);
        Assert.Equal([20], result[1].Values);
    }

    [GroupKeyColumns("Region")]
    public class TypeAndPathRulePriority : IDbReadable {
        public TypeAndPathRulePriority([GroupKey] DateTime date, List<int> amounts) {
            Date = date;
            Region = null!;
            Amounts = amounts;
        }
        public TypeAndPathRulePriority(string region, List<int> amounts) {
            Date = default;
            Region = region;
            Amounts = amounts;
        }
        public DateTime Date { get; }
        public string Region { get; }
        public List<int> Amounts { get; }
    }

    [Fact]
    [DocumentationExample("grouping.md", "grouping-precedence")]
    public void A_path_level_key_has_priority_over_the_type_level_key() {
        var d1 = new DateTime(2026, 7, 30);
        var d2 = new DateTime(2026, 7, 31);
        ColumnInfo[] cols = [new("Date", typeof(DateTime), false), new("Amounts", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TypeAndPathRulePriority>>(cols);
        using var reader = Rows.Reader(cols, [d1, 100], [d1, 200], [d2, 300]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal(d1, result[0].Date);
        Assert.Equal([100, 200], result[0].Amounts);
        Assert.Equal(d2, result[1].Date);
        Assert.Equal([300], result[1].Amounts);
    }

    [Fact]
    public void A_construction_with_no_path_key_uses_the_type_level_key() {
        ColumnInfo[] cols = [new("Region", typeof(string), false), new("Amounts", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<TypeAndPathRulePriority>>(cols);
        using var reader = Rows.Reader(cols, ["West", 100], ["West", 200], ["East", 300]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("West", result[0].Region);
        Assert.Equal([100, 200], result[0].Amounts);
        Assert.Equal("East", result[1].Region);
        Assert.Equal([300], result[1].Amounts);
    }

    // --- collapse path -----------------------------------------------------------------------------------

    public sealed record KeyedPoint([property: GroupKey] int Id, string Name) : IDbReadable;

    [Fact]
    public void An_all_simple_type_collapses_and_its_key_does_not_span() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var parser = Assert.IsType<DefaultTypeParserMaker>(TypeParser.DefaultTypeParserMaker).ForceMultiRow<KeyedPoint>(TypeParser.GetDefaultNullColHandler<KeyedPoint>(), cols);
        using var reader = Rows.Reader(cols, [1, "one"], [1, "two"]);
        reader.Read();

        var first = parser.Parse(reader);
        Assert.True(first.CanContinue);
        Assert.Equal(new KeyedPoint(1, "one"), first.Result);
        var second = parser.Parse(reader);
        Assert.False(second.CanContinue);
        Assert.Equal(new KeyedPoint(1, "two"), second.Result);
    }

    // --- grouping rule architecture: independence from storage, multiple params, generics, same impl --------

    public sealed class MemberKeyNotKept : IDbReadable {
        public MemberKeyNotKept(string data, List<int> values) {
            Data = data;
            Values = values;
        }
        public string Data { get; }
        public List<int> Values { get; }
        [GroupKey] public int Id { get; }
    }

    [Fact]
    public void Grouping_rule_on_a_member_is_independent_from_storage() {
        ColumnInfo[] cols = [new("Id", typeof(int), false), new("Data", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<MemberKeyNotKept>>(cols);
        using var reader = Rows.Reader(cols, [1, "x", 10], [1, "x", 11], [2, "y", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("x", result[0].Data);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal(0, result[0].Id);
    }

    public sealed class ParameterKeyUnused : IDbReadable {
        public ParameterKeyUnused(string data, List<int> values) {
            Data = data;
            Values = values;
        }
        public string Data { get; }
        public List<int> Values { get; }
    }


    public sealed class MultiParamBoundary : IDbReadable {
        public MultiParamBoundary(int a, string b, DateTime c, List<int> values) {
            A = a;
            B = b;
            C = c;
            Values = values;
        }
        public int A { get; }
        public string B { get; }
        public DateTime C { get; }
        public List<int> Values { get; }
        [GroupKey]
        public static (bool Same, (int, string, DateTime) Next) BySeveralParams((int, string, DateTime) previous, int a, string b, DateTime c)
            => ((a, b, c) == previous, (a, b, c));
    }

    [Fact]
    public void Grouping_method_supports_multiple_negotiated_parameters() {
        var d1 = new DateTime(2026, 7, 30);
        var d2 = new DateTime(2026, 7, 31);
        ColumnInfo[] cols = [
            new("A", typeof(int), false),
            new("B", typeof(string), false),
            new("C", typeof(DateTime), false),
            new("Values", typeof(int), false)
        ];
        var parser = TypeParser.GetTypeParser<List<MultiParamBoundary>>(cols);
        using var reader = Rows.Reader(cols, [1, "x", d1, 10], [1, "x", d1, 11], [1, "y", d1, 12], [2, "x", d2, 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(3, result.Count);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal([12], result[1].Values);
        Assert.Equal([20], result[2].Values);
    }

    public sealed class GenericMemberKey<T> : IDbReadable {
        public GenericMemberKey(T key, string data, List<int> values) {
            Key = key;
            Data = data;
            Values = values;
        }
        [GroupKey] public T Key { get; }
        public string Data { get; }
        public List<int> Values { get; }
    }

    [Fact]
    public void Grouping_rule_on_a_generic_member_resolves_to_the_spanning_type() {
        ColumnInfo[] cols = [new("Key", typeof(int), false), new("Data", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<GenericMemberKey<int>>>(cols);
        using var reader = Rows.Reader(cols, [1, "x", 10], [1, "x", 11], [2, "y", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("x", result[0].Data);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal("y", result[1].Data);
        Assert.Equal([20], result[1].Values);
    }

    public sealed class MethodWithAltParameter : IDbReadable {
        public MethodWithAltParameter(int value, List<int> items) {
            Value = value;
            Items = items;
        }
        public int Value { get; }
        public List<int> Items { get; }
        [GroupKey]
        public static (bool Same, int Next) GroupByAlternateColumn(int previous, [Alt("GroupId")] int group)
            => (group == previous, group);
    }

    [Fact]
    public void Grouping_method_parameters_support_alt_attribute() {
        ColumnInfo[] cols = [new("GroupId", typeof(int), false), new("Value", typeof(int), false), new("Items", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<MethodWithAltParameter>>(cols);
        using var reader = Rows.Reader(cols, [1, 100, 10], [1, 200, 11], [2, 300, 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal([10, 11], result[0].Items);
        Assert.Equal([20], result[1].Items);
    }

    public sealed class StaticReadOnlyKeyField : IDbReadable {
        public StaticReadOnlyKeyField(string data, List<int> values) {
            Data = data;
            Values = values;
        }
        public string Data { get; }
        public List<int> Values { get; }
        [GroupKey] public static readonly int GroupId = 0;
    }

    [Fact]
    public void Grouping_rule_on_a_static_readonly_field_groups_by_that_column() {
        ColumnInfo[] cols = [new("GroupId", typeof(int), false), new("Data", typeof(string), false), new("Values", typeof(int), false)];
        var parser = TypeParser.GetTypeParser<List<StaticReadOnlyKeyField>>(cols);
        using var reader = Rows.Reader(cols, [1, "x", 10], [1, "x", 11], [2, "y", 20]);
        reader.Read();
        var result = parser.Parse(reader).Result;

        Assert.Equal(2, result.Count);
        Assert.Equal("x", result[0].Data);
        Assert.Equal([10, 11], result[0].Values);
        Assert.Equal("y", result[1].Data);
        Assert.Equal([20], result[1].Values);
    }

    public sealed class MemberKeyVersion : IDbReadable {
        public MemberKeyVersion(int value, List<int> items) {
            Value = value;
            Items = items;
        }
        [GroupKey] public int Group { get; set; }
        public int Value { get; }
        public List<int> Items { get; }
    }


}
