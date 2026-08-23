using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rinku.Mapping;
using Rinku.Mapping.Defaults;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.Mapping;

[Collection("GlobalMappingConfiguration")]
public class ConfigurationCacheTests {
    [Fact]
    [DocumentationExample("result-parsers.md", "global-parser-invalidation")]
    public void Schema_invalidation_asks_a_retaining_command_before_disposal() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var query = new QueryCommand("SELECT Value");
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            ColumnInfo[] equivalent = [new("Renamed", typeof(int), false)];
            var first = TypeParser.GetTypeParser<DisposableValue>(columns);
            query.UpdateParseCache([], first);

            Assert.True(TypeParser.Invalidate(equivalent, ParserInvalidationMode.CheckUsage) >= 1);

            Assert.False(((DisposableValueParser)first).IsDisposed);
            Assert.True(query.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out var retained));
            Assert.Same(first, retained);
            Assert.NotSame(first, TypeParser.GetTypeParser<DisposableValue>(columns));

            query.Dispose();
            Assert.True(((DisposableValueParser)first).IsDisposed);
        }
        finally {
            query.Dispose();
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Reference_invalidation_removes_the_exact_parser_from_a_command_before_disposal() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var query = new QueryCommand("SELECT Value");
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            var parser = TypeParser.GetTypeParser<DisposableValue>(columns);
            query.UpdateParseCache([], parser);

            Assert.True(TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences));

            Assert.False(query.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.True(((DisposableValueParser)parser).IsDisposed);
        }
        finally {
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_local_invalidation_releases_only_its_own_parser_reference() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var firstCommand = new QueryCommand("SELECT Value");
        using var secondCommand = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            firstCommand.UpdateParseCache([], parser);
            secondCommand.UpdateParseCache([], parser);

            Assert.Equal(1, firstCommand.InvalidateParsers(QueryParserInvalidationScope.Local));

            Assert.False(firstCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.True(secondCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out var retained));
            Assert.Same(parser, retained);
            Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.False(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_global_if_unused_keeps_the_global_parser_while_another_cache_uses_it() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var firstCommand = new QueryCommand("SELECT Value");
        using var secondCommand = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            firstCommand.UpdateParseCache([], parser);
            secondCommand.UpdateParseCache([], parser);

            Assert.Equal(1, firstCommand.InvalidateParsers(QueryParserInvalidationScope.GlobalIfUnused));

            Assert.False(firstCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.True(secondCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out var retained));
            Assert.Same(parser, retained);
            Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.False(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_default_invalidation_removes_and_disposes_a_parser_when_it_is_the_only_user() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var query = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            query.UpdateParseCache([], parser);

            Assert.Equal(1, query.InvalidateParsers());

            Assert.False(query.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.DoesNotContain(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.True(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Disposing_a_query_command_removes_its_global_parser_when_it_is_the_only_user() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        var query = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            query.UpdateParseCache([], parser);

            query.Dispose();

            Assert.DoesNotContain(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.True(parser.IsDisposed);
        }
        finally {
            query.Dispose();
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_global_invalidation_forces_every_cache_to_release_the_exact_parser() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var firstCommand = new QueryCommand("SELECT Value");
        using var secondCommand = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            firstCommand.UpdateParseCache([], parser);
            secondCommand.UpdateParseCache([], parser);

            Assert.Equal(1, firstCommand.InvalidateParsers(QueryParserInvalidationScope.Global));

            Assert.False(firstCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.False(secondCommand.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.DoesNotContain(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.True(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_invalidation_rejects_an_unknown_scope() {
        using var query = new QueryCommand("SELECT Value");
        Assert.Throws<ArgumentOutOfRangeException>(() => query.InvalidateParsers((QueryParserInvalidationScope)42));
    }

    [Fact]
    public void Query_command_can_invalidate_one_exact_parser_without_touching_its_other_parsers() {
        var maker = new DisposableValueMaker();
        var otherMaker = new SchemaIndependentMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        TypeParser.TypeParserMakers.Insert(0, otherMaker);
        using var query = new QueryCommand("SELECT Value");
        DisposableValueParser? selected = null;
        ITypeParser<SchemaIndependentValue>? other = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            selected = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            other = TypeParser.GetTypeParser<SchemaIndependentValue>(columns);
            query.UpdateParseCache([], selected);
            query.UpdateParseCache([], other, resultSetIndex: 1);

            Assert.Equal(1, query.InvalidateParser(selected, QueryParserInvalidationScope.Local));

            Assert.False(query.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.True(query.TryGetCachedParser<SchemaIndependentValue>(Span<bool>.Empty, out var retained, resultSetIndex: 1));
            Assert.Same(other, retained);
            Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, selected));
            Assert.False(selected.IsDisposed);
        }
        finally {
            if (selected is not null)
                TypeParser.Invalidate(selected, ParserInvalidationMode.InvalidateReferences);
            if (other is not null)
                TypeParser.Invalidate(other, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(otherMaker);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_individual_invalidation_defaults_to_global_if_unused() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var query = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
            query.UpdateParseCache([], parser);

            Assert.Equal(1, query.InvalidateParser(parser));

            Assert.False(query.TryGetCachedParser<DisposableValue>(Span<bool>.Empty, out _));
            Assert.DoesNotContain(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.True(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Query_command_does_not_invalidate_an_individual_parser_it_does_not_retain() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        using var query = new QueryCommand("SELECT Value");
        DisposableValueParser? parser = null;
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);

            Assert.Equal(0, query.InvalidateParser(parser, QueryParserInvalidationScope.Global));
            Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
            Assert.False(parser.IsDisposed);
        }
        finally {
            if (parser is not null)
                TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences);
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Registration_changes_do_not_remove_or_notify_existing_parser_caches() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];
        var first = TypeParser.GetTypeParser<CacheValue>(columns);
        using var query = new QueryCommand("SELECT Value");
        query.UpdateParseCache([], first);
        var original = TypeParsingInfo.GetOrAdd<CacheValue>();
        var globalCache = TypeParser.ReadingInfos;
        int notifications = 0;
        void OnParserDisposing(object? _, ParserDisposingEventArgs __) => notifications++;
        TypeParser.ParserDisposing += OnParserDisposing;

        try {
            TypeParsingInfo.AddOrSet(typeof(CacheValue), new DefaultTypeParsingInfo(typeof(CacheValue)));

            Assert.Same(globalCache, TypeParser.ReadingInfos);
            Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, first));
            Assert.Equal(0, notifications);
            Assert.True(query.TryGetCachedParser<CacheValue>(Span<bool>.Empty, out var retained));
            Assert.Same(first, retained);
            using var reader = Rows.Reader(columns, [42]);
            Assert.True(reader.Read());
            Assert.Equal(new CacheValue(42), first.Parse(reader).Result);
        }
        finally {
            TypeParser.ParserDisposing -= OnParserDisposing;
            TypeParsingInfo.AddOrSet(typeof(CacheValue), original);
            TypeParser.Invalidate(columns, ParserInvalidationMode.InvalidateReferences);
        }
    }

    [Fact]
    [DocumentationExample("result-parsers.md", "parser-disposing-event")]
    public void Disposing_event_reports_the_exact_parser_and_runs_before_disposal() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        ColumnInfo[] columns = [new("Value", typeof(int), false)];
        var parser = (DisposableValueParser)TypeParser.GetTypeParser<DisposableValue>(columns);
        ParserDisposingEventArgs? received = null;
        bool wasDisposedDuringEvent = true;
        void Handler(object? _, ParserDisposingEventArgs args) {
            received = args;
            wasDisposedDuringEvent = parser.IsDisposed;
            args.Cancel = true;
        }
        TypeParser.ParserDisposing += Handler;
        try {
            Assert.True(TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences));
        }
        finally {
            TypeParser.ParserDisposing -= Handler;
            TypeParser.TypeParserMakers.Remove(maker);
        }

        Assert.NotNull(received);
        Assert.Same(parser, received.Parser);
        Assert.Equal(ParserInvalidationMode.InvalidateReferences, received.Mode);
        Assert.False(wasDisposedDuringEvent);
        Assert.True(parser.IsDisposed);
    }

    [Fact]
    public void Disposing_a_query_command_disposes_its_owned_mapper() {
        var query = new QueryCommand("SELECT @Value");
        var liveKeys = query.Mapper.GetKeysArray();

        query.Dispose();

        Assert.NotSame(liveKeys, query.Mapper.GetKeysArray());
        Assert.Null(query.Mapper.GetKeysArray()[0]);
        query.Dispose();
    }

    [Fact]
    public void Invalidating_a_multi_row_parser_releases_generated_static_mapper_targets() {
        ColumnInfo[] columns = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        var parser = TypeParser.GetTypeParser<List<RinkuLib.Tests.DbParsing.MultiRowTests.DynaHolder>>(columns);
        Assert.Contains(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, parser));
        var stateType = parser.GetType().GetGenericArguments()[1];
        var targetFields = stateType.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(object[])).ToArray();
        Assert.Contains(targetFields, field => field.GetValue(null) is object[] targets && targets.Any(target => target is Mapper));

        Assert.True(TypeParser.Invalidate(columns, ParserInvalidationMode.InvalidateReferences) >= 1);

        Assert.DoesNotContain(targetFields, field => field.GetValue(null) is object[] targets && targets.Any(target => target is Mapper));
    }

    [Fact]
    public void Cached_type_parser_cancels_checked_disposal_until_it_releases_its_reference() {
        var maker = new DisposableValueMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        ColumnInfo[] columns = [new("Value", typeof(int), false)];
        using var cache = new CachedTypeParser<DisposableValue>();
        using var command = new FakeCommand();
        using var reader = Rows.Reader(columns, [1]);
        try {
            var parser = (DisposableValueParser)cache.UpdateCache(command, reader);

            Assert.True(TypeParser.Invalidate(columns, ParserInvalidationMode.CheckUsage) >= 1);
            Assert.False(parser.IsDisposed);
            Assert.True(cache.Invalidate());
            Assert.True(parser.IsDisposed);
            Assert.False(cache.Invalidate());
        }
        finally {
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Invalidation_mode_rejects_values_outside_the_two_policies() {
        Assert.Throws<ArgumentOutOfRangeException>(() => TypeParser.InvalidateAll((ParserInvalidationMode)42));
    }

    [Fact]
    [DocumentationExample("result-parsers.md", "custom-result-parser")]
    [DocumentationExample("result-parsers.md", "register-result-parser")]
    public void Documented_custom_Last_shape_reads_the_final_row() {
        var maker = new ReusingBaseTypeParserMaker([typeof(Last<>)], (definition, itemType, ref _) => typeof(LastParser<>).MakeGenericType(itemType));
        TypeParser.TypeParserMakers.Insert(0, maker);
        try {
            ColumnInfo[] columns = [new("Value", typeof(int), false)];
            using var reader = Rows.Reader(columns, [1], [2], [3]);
            var parser = TypeParser.GetTypeParser<Last<int>>(columns);

            Assert.True(reader.Read());
            Assert.Equal(3, parser.Parse(reader).Result.Value);
        }
        finally {
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Exact_global_invalidation_rebuilds_without_touching_a_checked_local_reference() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];
        var first = TypeParser.GetTypeParser<CacheValue>(columns);
        using var query = new QueryCommand("SELECT Value");
        query.UpdateParseCache([], first);

        Assert.True(TypeParser.Invalidate(first, ParserInvalidationMode.CheckUsage));
        var second = TypeParser.GetTypeParser<CacheValue>(columns);

        Assert.NotSame(first, second);
        Assert.DoesNotContain(TypeParser.ReadingInfos, entry => ReferenceEquals(entry.Parser, first));
        Assert.True(query.TryGetCachedParser<CacheValue>(Span<bool>.Empty, out var retained));
        Assert.Same(first, retained);
    }

    [Fact]
    [DocumentationExample("result-parsers.md", "schema-independent-parser")]
    public void Parser_decides_whether_different_schemas_share_one_global_entry() {
        var maker = new SchemaIndependentMaker();
        TypeParser.TypeParserMakers.Insert(0, maker);
        try {
            ColumnInfo[] firstSchema = [new("First", typeof(int), false)];
            ColumnInfo[] secondSchema = [new("Other", typeof(string), true), new("Extra", typeof(Guid), false)];

            var first = TypeParser.GetTypeParser<SchemaIndependentValue>(firstSchema);
            var second = TypeParser.GetTypeParser<SchemaIndependentValue>(secondSchema);

            Assert.Same(first, second);
            Assert.Equal(1, maker.BuildCount);
            Assert.True(first.CanParse(firstSchema));
            Assert.True(first.CanParse(secondSchema));
        }
        finally {
            TypeParser.TypeParserMakers.Remove(maker);
        }
    }

    [Fact]
    public void Positional_tuple_reuses_generated_parser_when_only_column_names_change() {
        ColumnInfo[] firstSchema = [new("Left", typeof(int), false), new("Right", typeof(int), false)];
        ColumnInfo[] secondSchema = [new("X", typeof(int), false), new("Y", typeof(int), false)];

        var first = TypeParser.GetTypeParser<(int, int)>(firstSchema);
        var second = TypeParser.GetTypeParser<(int, int)>(secondSchema);
        var firstValue = Rows.ParseOne<(int, int)>(firstSchema, 1, 2);
        var secondValue = Rows.ParseOne<(int, int)>(secondSchema, 3, 4);

        Assert.Same(first, second);
        Assert.True(first.CanParse(secondSchema));
        Assert.Equal((1, 2), firstValue);
        Assert.Equal((3, 4), secondValue);
    }

    [Fact]
    [DocumentationExample("result-parsers.md", "get-parser")]
    public void Direct_parser_reads_a_reader_positioned_on_its_first_row() {
        ColumnInfo[] columns = [new("Value", typeof(int), false)];
        ITypeParser<CacheValue> parser = TypeParser.GetTypeParser<CacheValue>(columns);
        using var reader = Rows.Reader(columns, [42]);

        CacheValue value = reader.Read()
            ? parser.Parse(reader).Result
            : parser.Default();

        Assert.Equal(new CacheValue(42), value);
    }

    [Fact]
    public void Mixed_tuple_composes_positional_and_named_schema_dependencies() {
        ColumnInfo[] firstSchema = [new("Position", typeof(int), false), new("Id", typeof(int), false), new("Score", typeof(int), false)];
        ColumnInfo[] renamedPosition = [new("Anything", typeof(int), false), new("Id", typeof(int), false), new("Score", typeof(int), false)];
        ColumnInfo[] wrongNestedOrder = [new("Anything", typeof(int), false), new("Score", typeof(int), false), new("Id", typeof(int), false)];

        var first = TypeParser.GetTypeParser<(int, MixedUser)>(firstSchema);
        var renamed = TypeParser.GetTypeParser<(int, MixedUser)>(renamedPosition);
        var firstValue = Rows.ParseOne<(int, MixedUser)>(firstSchema, 1, 10, 20);
        var renamedValue = Rows.ParseOne<(int, MixedUser)>(renamedPosition, 2, 30, 40);

        Assert.Same(first, renamed);
        Assert.True(first.CanParse(renamedPosition));
        Assert.Equal(1, firstValue.Item1);
        Assert.Equal(new MixedUser(10, 20), firstValue.Item2);
        Assert.Equal(2, renamedValue.Item1);
        Assert.Equal(new MixedUser(30, 40), renamedValue.Item2);
        Assert.False(first.CanParse(wrongNestedOrder));
        Assert.Throws<RinkuNoParserException>(() => TypeParser.GetTypeParser<(int, MixedUser)>(wrongNestedOrder));
    }

    [Fact]
    public void Direct_object_mapping_finds_named_members_without_tuple_sequential_order() {
        ColumnInfo[] swappedMembers = [new("Score", typeof(int), false), new("Id", typeof(int), false)];

        var user = Rows.ParseOne<MixedUser>(swappedMembers, 20, 10);

        Assert.Equal(10, user.Id);
        Assert.Equal(20, user.Score);
    }

    [Fact]
    public void Alternative_names_reuse_when_they_emit_the_same_final_parser() {
        ColumnInfo[] originalName = [new("Id", typeof(int), false), new("Name", typeof(string), false)];
        ColumnInfo[] alternativeName = [new("Id", typeof(int), false), new("Label", typeof(string), false)];

        var first = TypeParser.GetTypeParser<AlternativeName>(originalName);
        var second = TypeParser.GetTypeParser<AlternativeName>(alternativeName);
        var original = Rows.ParseOne<AlternativeName>(originalName, 1, "first");
        var alternative = Rows.ParseOne<AlternativeName>(alternativeName, 2, "second");

        Assert.Same(first, second);
        Assert.Same(((ISimpleParser<AlternativeName>)first).RowParser, ((ISimpleParser<AlternativeName>)second).RowParser);
        Assert.True(first.CanParse(alternativeName));
        Assert.Equal(new AlternativeName(1, "first"), original);
        Assert.Equal(new AlternativeName(2, "second"), alternative);
    }

    [Fact]
    public void Equal_instructions_do_not_reuse_when_bound_mapper_state_differs() {
        ColumnInfo[] firstSchema = [new("First", typeof(int), false)];
        ColumnInfo[] secondSchema = [new("Second", typeof(int), false)];

        var first = TypeParser.GetTypeParser<DynaObject>(firstSchema);
        var second = TypeParser.GetTypeParser<DynaObject>(secondSchema);
        var firstValue = Rows.ParseOne<DynaObject>(firstSchema, 10);
        var secondValue = Rows.ParseOne<DynaObject>(secondSchema, 20);

        Assert.NotSame(first, second);
        Assert.False(first.CanParse(secondSchema));
        Assert.Equal(10, firstValue.Get<int>("First"));
        Assert.Equal(20, secondValue.Get<int>("Second"));
        Assert.False(firstValue.ContainsKey("Second"));
        Assert.False(secondValue.ContainsKey("First"));
    }

    [Fact]
    public void Named_object_does_not_reuse_parser_when_member_columns_swap() {
        ColumnInfo[] firstSchema = [new("Left", typeof(int), false), new("Right", typeof(int), false)];
        ColumnInfo[] swappedSchema = [new("Right", typeof(int), false), new("Left", typeof(int), false)];

        var first = TypeParser.GetTypeParser<NamedPair>(firstSchema);
        var swapped = TypeParser.GetTypeParser<NamedPair>(swappedSchema);
        var firstValue = Rows.ParseOne<NamedPair>(firstSchema, 1, 2);
        var swappedValue = Rows.ParseOne<NamedPair>(swappedSchema, 20, 10);

        Assert.NotSame(first, swapped);
        Assert.False(first.CanParse(swappedSchema));
        Assert.Equal(new NamedPair(1, 2), firstValue);
        Assert.Equal(new NamedPair(10, 20), swappedValue);
    }

    private sealed record CacheValue(int Value) : IDbReadable;
    private sealed record NamedPair(int Left, int Right) : IDbReadable;
    private sealed record MixedUser(int Id, int Score) : IDbReadable;
    private sealed record AlternativeName(int Id, [Alt("Label")] string Name) : IDbReadable;
    private sealed record SchemaIndependentValue;
    private sealed record DisposableValue(int Value);

    private sealed class DisposableValueMaker : ITypeParserMaker {
        public bool CanHandle<T>() => typeof(T) == typeof(DisposableValue);

        public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
            if (typeof(T) != typeof(DisposableValue) || cols.Length != 1 || cols[0].Type != typeof(int)) {
                parser = null;
                return false;
            }
            parser = (ITypeParser<T>)(object)new DisposableValueParser();
            return true;
        }
    }

    private sealed class DisposableValueParser : BaseTypeParser<DisposableValue> {
        internal bool IsDisposed;
        public override bool CanParse(ColumnInfo[] schema) => schema is [{ Type: var type }] && type == typeof(int);
        public override CommandBehavior Behavior => CommandBehavior.SingleRow | CommandBehavior.SingleResult;
        public override DisposableValue Default() => new(0);
        public override (bool CanContinue, DisposableValue Result) Parse(DbDataReader reader)
            => reader.Read() ? (true, new(reader.GetInt32(0))) : (false, Default());
        public override async ValueTask<(bool CanContinue, DisposableValue Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default)
            => await reader.ReadAsync(ct) ? (true, new(reader.GetInt32(0))) : (false, Default());
        public override void Dispose() => IsDisposed = true;
    }

    private sealed class SchemaIndependentMaker : ITypeParserMaker {
        private readonly SchemaIndependentParser Parser = new();
        internal int BuildCount;

        public bool CanHandle<T>() => typeof(T) == typeof(SchemaIndependentValue);

        public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
            if (typeof(T) != typeof(SchemaIndependentValue)) {
                parser = null;
                return false;
            }
            BuildCount++;
            parser = (ITypeParser<T>)(object)Parser;
            return true;
        }
    }

    private sealed class SchemaIndependentParser : BaseTypeParser<SchemaIndependentValue> {
        public override bool CanParse(ColumnInfo[] schema) => true;
        public override CommandBehavior Behavior => CommandBehavior.SingleRow | CommandBehavior.SingleResult;
        public override SchemaIndependentValue Default() => new();
        public override (bool CanContinue, SchemaIndependentValue Result) Parse(DbDataReader reader) => (reader.Read(), new());
        public override async ValueTask<(bool CanContinue, SchemaIndependentValue Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) => (await reader.ReadAsync(ct), new());
    }

    private readonly record struct Last<T>(T Value);

    private sealed class LastParser<T>(ITypeParser<T> element) : BaseTypeParser<Last<T>> {
        public override bool CanParse(ColumnInfo[] schema) => element.CanParse(schema);
        public override CommandBehavior Behavior => element.Behavior & ~CommandBehavior.SingleRow;
        public override Last<T> Default() => throw new RinkuNoRowsException();

        public override (bool CanContinue, Last<T> Result) Parse(DbDataReader reader) {
            (bool more, T value) = element.Parse(reader);
            while (more)
                (more, value) = element.Parse(reader);
            return (false, new Last<T>(value));
        }

        public override async ValueTask<(bool CanContinue, Last<T> Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
            (bool more, T value) = await element.ParseAsync(reader, ct);
            while (more)
                (more, value) = await element.ParseAsync(reader, ct);
            return (false, new Last<T>(value));
        }
    }
}
