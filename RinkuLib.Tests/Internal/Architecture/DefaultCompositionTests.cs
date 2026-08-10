using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace RinkuLib.Tests.Internal.Architecture;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DefaultCompositionCollection {
    public const string Name = "Default composition";
}

[Collection(DefaultCompositionCollection.Name)]
public class DefaultCompositionTests {
    private sealed class FactoryOwned;
    private sealed class ParserOwned;

    private sealed class ProbeInfo : TypeParsingInfo {
        public override void ValidateCanUseType(Type targetType) {
            if (targetType != typeof(FactoryOwned))
                throw new InvalidOperationException();
        }

        public override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages, ParamInfo paramInfo,
            ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage,
            MethodCtorInfo.AdditionalFlags callerFlags = default) => null;
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

    private sealed class ProbeInfoFactory(ITypeParsingInfoFactory inner) : ITypeParsingInfoFactory {
        public int CreateCalls { get; private set; }
        public TypeParsingInfo Scalar => inner.Scalar;
        public TypeParsingInfo Array => inner.Array;
        public TypeParsingInfo Created { get; } = new ProbeInfo();
        public TypeParsingInfo Create(Type type) {
            CreateCalls++;
            return type == typeof(FactoryOwned) ? Created : inner.Create(type);
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
    public void Type_registry_delegates_missing_entries_to_the_installed_factory() {
        var original = TypeParsingInfo.DefaultFactory;
        var probe = new ProbeInfoFactory(original);
        try {
            TypeParsingInfo.DefaultFactory = probe;
            var resolved = TypeParsingInfo.ForceGet(typeof(FactoryOwned));
            Assert.Same(probe.Created, resolved);
            Assert.Equal(1, probe.CreateCalls);
        }
        finally {
            TypeParsingInfo.DefaultFactory = original;
        }
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
        Assert.Equal(typeof(ITypeParsingInfoFactory), typeof(TypeParsingInfo).GetProperty(nameof(TypeParsingInfo.DefaultFactory))!.PropertyType);
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
