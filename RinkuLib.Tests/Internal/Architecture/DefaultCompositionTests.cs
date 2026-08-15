using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Rinku.Mapping.Parsers.Defaults;
using RinkuLib.Tests.Infrastructure;
using Xunit;

namespace RinkuLib.Tests.Internal.Architecture;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DefaultCompositionCollection {
    public const string Name = "Default composition";
}

[Collection(DefaultCompositionCollection.Name)]
public class DefaultCompositionTests {
    private sealed class ParserOwned;
    private sealed record ListItem(int Value) : IDbReadable;
    private sealed class RecursiveBag<T> {
        public List<T> Items { get; } = [];
        public void Add(T item) => Items.Add(item);
    }
    private struct ValueBag<T> {
        public List<T> Items { get; } = [];
        public ValueBag() { }
        public void Add(T item) => Items.Add(item);
    }
    private static class ReadOnlyBag<T> {
        public static IReadOnlyCollection<T> Finish(List<T> items) => items;
    }

    private sealed class RejectingListInfo : TypeParsingInfo {
        public int Negotiations { get; private set; }

        public override void ValidateCanUseType(Type targetType) {
            if (targetType != typeof(List<>))
                throw new InvalidOperationException();
        }

        public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo,
            ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage,
            MethodCtorInfo.AdditionalFlags callerFlags = default) {
            Negotiations++;
            return null;
        }
    }

    private sealed class ProbeParser : BaseTypeParser<ParserOwned> {
        public override CommandBehavior Behavior => CommandBehavior.SingleResult | CommandBehavior.SingleRow;
        public override bool CanParse(ColumnInfo[] schema) => true;
        public override ParserOwned Default() => new();
        public override (bool CanContinue, ParserOwned Result) Parse(DbDataReader reader) => (false, new());
        public override ValueTask<(bool CanContinue, ParserOwned Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default)
            => new((false, new ParserOwned()));
    }

    private sealed class ProbeParserMaker : ITypeParserMaker {
        public ITypeParser<ParserOwned> Parser { get; } = new ProbeParser();
        public int MakeCalls { get; private set; }
        public bool CanHandle<T>() => typeof(T) == typeof(ParserOwned);
        public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
            MakeCalls++;
            parser = typeof(T) == typeof(ParserOwned) ? (ITypeParser<T>)(object)Parser : null!;
            return typeof(T) == typeof(ParserOwned);
        }
    }

    private sealed class ProbeParamInfo : DbParamInfo {
        public ProbeParamInfo() : base(false) { }
        public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) => true;
        public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) => true;
        public override bool Use(string paramName, IDbCommand cmd, object value) => true;
        public override void Remove(IDbCommand cmd, object currentValue) { }
        public override bool Use(string paramName, DbCommand cmd, object value) => true;
    }

    private sealed class ProbeParameterDefaults(IDbParameterDefaults inner) : IDbParameterDefaults {
        private readonly DbParamInfo _inferred = new ProbeParamInfo();
        public int InferredReads { get; private set; }
        public DbParamInfo Inferred {
            get {
                InferredReads++;
                return _inferred;
            }
        }
        public DbParamInfo MakeInfo(System.Data.IDbDataParameter parameter) => inner.MakeInfo(parameter);
    }

    [Fact]
    public void Parser_cache_delegates_an_unclaimed_shape_to_the_installed_fallback() {
        var original = TypeParser.DefaultTypeParserMaker;
        var probe = new ProbeParserMaker();
        try {
            TypeParser.DefaultTypeParserMaker = probe;
            var resolved = TypeParser.GetTypeParser<ParserOwned>([]);
            Assert.Same(probe.Parser, resolved);
            Assert.Equal(1, probe.MakeCalls);
        }
        finally {
            TypeParser.DefaultTypeParserMaker = original;
        }
    }

    [Fact]
    public void Root_list_mapping_uses_its_registered_multi_row_contract() {
        var original = TypeParsingInfo.Get(typeof(List<>))!;
        var probe = new RejectingListInfo();
        try {
            TypeParsingInfo.AddOrSet(typeof(List<>), probe);

            Assert.Throws<RinkuNoParserException>(() => TypeParser.GetTypeParser<List<int>>([new("Value", typeof(int), false)]));
            Assert.Equal(1, probe.Negotiations);
        }
        finally {
            TypeParsingInfo.AddOrSet(typeof(List<>), original);
        }
    }

    [Fact]
    public void Shipped_simple_list_uses_the_generated_accumulator() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];

        var parser = TypeParser.GetTypeParser<List<ListItem>>(columns);

        Assert.True(parser.GetType().IsGenericType);
        Assert.Equal(typeof(MultiRowCollectionTypeParser<,>), parser.GetType().GetGenericTypeDefinition());
    }

    [Fact]
    public void Nested_list_is_composed_from_accumulator_plans() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];

        var parser = TypeParser.GetTypeParser<List<List<ListItem>>>(columns);

        Assert.True(parser.GetType().IsGenericType);
        Assert.Equal(typeof(RecursiveAccumulatorTypeParser<,,,>), parser.GetType().GetGenericTypeDefinition());
    }

    [Fact]
    public async Task Recursive_accumulator_composition_supports_lists_arrays_interfaces_and_custom_types() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];

        using (var listReader = Rows.Reader(columns, [1], [2], [3])) {
            var parser = TypeParser.GetTypeParser<List<List<List<int>>>>(columns);
            await listReader.ReadAsync(TestContext.Current.CancellationToken);
            var result = (await parser.ParseAsync(listReader, TestContext.Current.CancellationToken)).Result;
            Assert.Equal([1, 2, 3], Assert.Single(Assert.Single(result)));
        }

        using (var arrayReader = Rows.Reader(columns, [4], [5])) {
            var parser = TypeParser.GetTypeParser<int[][]>(columns);
            arrayReader.Read();
            Assert.Equal([4, 5], Assert.Single(parser.Parse(arrayReader).Result));
        }

        var bagType = typeof(RecursiveBag<>);
        var original = TypeParsingInfo.Get(bagType);
        var bagInfo = new MultiRowTypeParsingInfo(bagType.GetConstructor(Type.EmptyTypes)!,
            bagType.GetMethod(nameof(RecursiveBag<int>.Add))!, null);
        try {
            TypeParsingInfo.AddOrSet(bagType, bagInfo);
            using var bagReader = Rows.Reader(columns, [6], [7]);
            var parser = TypeParser.GetTypeParser<RecursiveBag<RecursiveBag<int>>>(columns);
            bagReader.Read();
            var outer = parser.Parse(bagReader).Result;
            Assert.Equal([6, 7], Assert.Single(outer.Items).Items);
        }
        finally {
            if (original is null)
                TypeParsingInfo.TryRemove(bagType, out _);
            else
                TypeParsingInfo.AddOrSet(bagType, original);
        }

        var valueBagType = typeof(ValueBag<>);
        original = TypeParsingInfo.Get(valueBagType);
        var valueBagInfo = new MultiRowTypeParsingInfo(valueBagType.GetConstructor(Type.EmptyTypes)!,
            valueBagType.GetMethod(nameof(ValueBag<int>.Add))!, null);
        try {
            TypeParsingInfo.AddOrSet(valueBagType, valueBagInfo);
            using var bagReader = Rows.Reader(columns, [8], [9]);
            var parser = TypeParser.GetTypeParser<ValueBag<ValueBag<int>>>(columns);
            bagReader.Read();
            var outer = parser.Parse(bagReader).Result;
            Assert.Equal([8, 9], Assert.Single(outer.Items).Items);
        }
        finally {
            if (original is null)
                TypeParsingInfo.TryRemove(valueBagType, out _);
            else
                TypeParsingInfo.AddOrSet(valueBagType, original);
        }

        var interfaceType = typeof(IReadOnlyCollection<>);
        original = TypeParsingInfo.Get(interfaceType);
        var interfaceInfo = new MultiRowTypeParsingInfo(typeof(List<>).GetConstructor(Type.EmptyTypes)!,
            typeof(List<>).GetMethod(nameof(List<int>.Add))!, typeof(ReadOnlyBag<>).GetMethod(nameof(ReadOnlyBag<int>.Finish))!);
        try {
            TypeParsingInfo.AddOrSet(interfaceType, interfaceInfo);
            using var interfaceReader = Rows.Reader(columns, [10], [11]);
            var parser = TypeParser.GetTypeParser<IReadOnlyCollection<IReadOnlyCollection<int>>>(columns);
            interfaceReader.Read();
            var outer = parser.Parse(interfaceReader).Result;
            Assert.Equal([10, 11], Assert.Single(outer));
        }
        finally {
            if (original is null)
                TypeParsingInfo.TryRemove(interfaceType, out _);
            else
                TypeParsingInfo.AddOrSet(interfaceType, original);
        }
    }

    [Fact]
    public void Parameter_ledger_uses_the_installed_initial_binding_strategy() {
        var original = DbParameterDefaults.Current;
        var probe = new ProbeParameterDefaults(original);
        try {
            DbParameterDefaults.Current = probe;
            var parameters = new QueryParameters(1, []);
            Assert.Same(probe.Inferred, parameters.VariablesInfo[0]);
        }
        finally {
            DbParameterDefaults.Current = original;
        }
    }

    [Fact]
    public void Warm_parameter_ledger_operations_do_not_resolve_the_default_again() {
        var original = DbParameterDefaults.Current;
        var probe = new ProbeParameterDefaults(original);
        try {
            DbParameterDefaults.Current = probe;
            var parameters = new QueryParameters(2, []);
            var coldReads = probe.InferredReads;

            Assert.True(parameters.NeedToCache(new object?[] { 1, null }));
            Assert.True(parameters.NeedToCache(new[] { true, false }.AsSpan()));
            Assert.False(parameters.IsCached(0));
            parameters.UpdateCachedIndexes();

            Assert.Equal(coldReads, probe.InferredReads);
        }
        finally {
            DbParameterDefaults.Current = original;
        }
    }

    [Fact]
    public void Composition_slots_are_typed_as_contracts() {
        Assert.Equal(typeof(ITypeParserMaker), typeof(TypeParser).GetProperty(nameof(TypeParser.DefaultTypeParserMaker))!.PropertyType);
        Assert.Equal(typeof(IDbParameterDefaults), typeof(DbParameterDefaults).GetProperty(nameof(DbParameterDefaults.Current))!.PropertyType);
    }

    [Fact]
    public void Runtime_global_usings_do_not_import_implementation_namespaces() {
        var globalUsings = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "RinkuLib", "GlobalUsings.cs"));
        Assert.DoesNotContain("Rinku.Mapping.Defaults", globalUsings);
        Assert.DoesNotContain("Rinku.Mapping.Parsers.Defaults", globalUsings);
        Assert.DoesNotContain("Rinku.Querying.Defaults", globalUsings);
    }

    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RinkuLib.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
