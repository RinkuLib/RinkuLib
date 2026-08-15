using System.Data;
using System.Data.Common;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Documentation;
using Xunit;

namespace RinkuLib.Tests.Execution;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalCacheDocumentationCollection {
    public const string Name = "Global cache documentation";
}

/// <summary>Executable examples for docs/articles/customization/caches.md.</summary>
[Collection(GlobalCacheDocumentationCollection.Name)]
public class CacheControlDocumentationTests(SqliteDb db) : IClassFixture<SqliteDb> {
    private sealed record Track(long Id, string Name);
    private sealed record TrackFilter(int id);

    private sealed class DisposalTrackingStreamCommand : FakeCommand {
        public int DisposeCount { get; private set; }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken ct)
            => Task.FromResult<DbDataReader>(Rows.Reader([new("Name", typeof(string), false)], ["John"], ["Victor"]));

        protected override void Dispose(bool disposing) {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private static DisposalTrackingStreamCommand StreamCommand() => new() { Connection = new FakeConnection() };

    [Fact]
    [DocumentationExample("caches.md", "invalidate-all")]
    [DocumentationExample("caches.md", "invalidate-one")]
    public void A_query_command_can_invalidate_all_or_one_of_its_parsers() {
        using var cnn = db.GetConnection();
        using var command = new QueryCommand("SELECT ID AS Id, Name FROM Users WHERE ID = @id");
        var run = command.StartBuilder();
        Assert.True(run.Use("@id", 1));

        Assert.Equal(new Track(1, "John"), run.Query<Track>(cnn));
        Assert.True(command.TryGetCachedParser<Track>(run.Variables, out var localParser));
        Assert.Equal(1, command.InvalidateParser(localParser, QueryParserInvalidationScope.Local));
        Assert.False(command.TryGetCachedParser<Track>(run.Variables, out _));

        Assert.Equal(new Track(1, "John"), run.Query<Track>(cnn));
        Assert.True(command.TryGetCachedParser<Track>(run.Variables, out var globalParser));
        Assert.Equal(1, command.InvalidateParser(globalParser, QueryParserInvalidationScope.Global));
        Assert.False(command.TryGetCachedParser<Track>(run.Variables, out _));

        Assert.Equal(new Track(1, "John"), run.Query<Track>(cnn));
        Assert.Equal(1, command.InvalidateParsers());
        Assert.False(command.TryGetCachedParser<Track>(run.Variables, out _));
    }

    [Fact]
    [DocumentationExample("caches.md", "parameter-accessors")]
    public void Direct_and_use_with_parameter_accessors_can_be_inspected_and_removed_separately() {
        using var command = new QueryCommand("SELECT ID AS Id, Name FROM Users WHERE ID = @id");

        Render.From(command, new TrackFilter(1));
        var builder = command.StartBuilder();
        builder.UseWith(new TrackFilter(2));
        Render.From(builder);

        var cached = Assert.Single(command.GetCachedParameterAccessors());
        Assert.Equal(typeof(TrackFilter), cached.ParameterType);
        Assert.Equal(ParameterAccessorKinds.Both, cached.Accessors);

        Assert.Equal(ParameterAccessorKinds.Direct,
            command.InvalidateParameterAccessor(typeof(TrackFilter), ParameterAccessorKinds.Direct));
        Assert.Equal(ParameterAccessorKinds.UseWith,
            Assert.Single(command.GetCachedParameterAccessors()).Accessors);

        Assert.Equal(ParameterAccessorKinds.UseWith,
            command.InvalidateParameterAccessor(typeof(TrackFilter), ParameterAccessorKinds.UseWith));
        Assert.Empty(command.GetCachedParameterAccessors());

        Render.From(command, new TrackFilter(3));
        builder.UseWith(new TrackFilter(4));
        Assert.Equal(ParameterAccessorKinds.Both,
            command.InvalidateParameterAccessor(typeof(TrackFilter), ParameterAccessorKinds.Both));
        Assert.Empty(command.GetCachedParameterAccessors());
    }

    [Fact]
    [DocumentationExample("caches.md", "remove-command")]
    [DocumentationExample("caches.md", "command-key")]
    public void The_public_command_dictionary_supports_sql_and_application_keys() {
        using var cnn = db.GetConnection();
        string sql = $"SELECT ID AS Id, Name FROM Users WHERE ID = 1 /* {Guid.NewGuid()} */";
        QueryCommand command = ConnectionQueryExtensions.GetOrCreateCommand(sql);

        Assert.Same(command, ConnectionQueryExtensions.CommandCache[sql]);
        Assert.True(ConnectionQueryExtensions.CommandCache.TryRemove(sql, out var removed));
        Assert.Same(command, removed);
        removed.Dispose();

        string key = "tracks.active." + Guid.NewGuid();
        var named = new QueryCommand("SELECT ID AS Id, Name FROM Users WHERE ID = 1");
        Assert.True(ConnectionQueryExtensions.CommandCache.TryAdd(key, named));
        try {
            Assert.Equal(new Track(1, "John"), cnn.Query<Track>(key));
        }
        finally {
            Assert.True(ConnectionQueryExtensions.CommandCache.TryRemove(key, out var cached));
            cached.Dispose();
        }
    }

    [Fact]
    [DocumentationExample("caches.md", "raw-command-parser")]
    public void Cached_type_parser_can_release_and_learn_its_parser_again() {
        using var cnn = db.Open();
        using var cache = new CachedTypeParser<List<Track>>();
        using DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT ID AS Id, Name FROM Users ORDER BY ID";

        List<Track> first = cache.Query(command);
        Assert.True(cache.Invalidate());
        List<Track> second = cache.Query(command);

        Assert.Equal(first, second);
        Assert.Equal(3, second.Count);
    }

    [Fact]
    [DocumentationExample("caches.md", "fixed-schema-parser")]
    public void Non_generic_cached_type_parser_keeps_one_parser_per_requested_type() {
        using var cnn = db.Open();
        using var cache = CachedTypeParser.From<Track>();
        using DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT ID AS Id, Name FROM Users ORDER BY ID";

        var tracks = cache.Get<List<Track>>();
        Assert.Same(tracks, cache.Get<List<Track>>());
        Assert.Equal(3, cache.Query<List<Track>>(command).Count);

        var first = cache.Query<DynaObject>(command);
        Assert.Equal(1L, first.Get<long>("Id"));

        Assert.True(cache.Invalidate<List<Track>>());
        Assert.Same(first.GetType(), cache.Query<DynaObject>(command).GetType());
    }

    [Fact]
    [DocumentationExample("caches.md", "cached-parser-combinations")]
    [DocumentationExample("caches.md", "learned-schema-parser")]
    public async Task Non_generic_cached_type_parser_can_learn_one_schema_then_cache_each_requested_type() {
        using var cnn = db.Open();
        using var cache = new CachedTypeParser();
        using DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT ID AS Id, Name FROM Users ORDER BY ID";

        Assert.False(cache.HasSchema);
        Assert.Throws<InvalidOperationException>(() => cache.Get<List<Track>>());

        List<Track> tracks = await cache.QueryAsync<List<Track>>(command, ct: TestContext.Current.CancellationToken);
        ColumnInfo[] learnedSchema = cache.Schema;

        Assert.True(cache.HasSchema);
        Assert.Equal(3, tracks.Count);
        Assert.Same(cache.Get<List<Track>>(), cache.Get<List<Track>>());

        DynaObject first = cache.Query<DynaObject>(command);
        Assert.Equal(1L, first.Get<long>("Id"));
        Assert.Equal(learnedSchema, cache.Schema);

        Assert.Equal(2, cache.Invalidate());
        Assert.True(cache.HasSchema);
        Assert.Equal(3, cache.Query<List<Track>>(command).Count);
        Assert.Equal(learnedSchema, cache.Schema);
    }

    [Fact]
    public async Task Non_generic_cached_type_parser_can_learn_its_schema_while_streaming() {
        using var cnn = db.Open();
        using var cache = new CachedTypeParser();
        using DbCommand command = cnn.CreateCommand();
        command.CommandText = "SELECT ID AS Id, Name FROM Users ORDER BY ID";
        var tracks = new List<Track>();

        await foreach (Track track in cache.StreamQueryAsync<Track>(command, disposeCommand: false, ct: TestContext.Current.CancellationToken))
            tracks.Add(track);

        Assert.True(cache.HasSchema);
        Assert.Equal(3, tracks.Count);
        Assert.Same(cache.Get<Track>(), cache.Get<Track>());
    }

    [Fact]
    public async Task Cached_parser_streams_leave_a_supplied_command_caller_owned_by_default() {
        var ct = TestContext.Current.CancellationToken;
        using var fixedType = new CachedTypeParser<string>();
        var fixedTypeCommand = StreamCommand();
        var fixedTypeRows = new List<string>();

        await foreach (string value in fixedType.StreamQueryAsync(fixedTypeCommand, ct: ct))
            fixedTypeRows.Add(value);

        Assert.Equal(["John", "Victor"], fixedTypeRows);
        Assert.Equal(0, fixedTypeCommand.DisposeCount);
        fixedTypeCommand.Dispose();
        Assert.Equal(1, fixedTypeCommand.DisposeCount);

        using var varyingType = new CachedTypeParser();
        var varyingTypeCommand = StreamCommand();
        await foreach (string _ in varyingType.StreamQueryAsync<string>(varyingTypeCommand, ct: ct)) { }
        Assert.Equal(0, varyingTypeCommand.DisposeCount);
        varyingTypeCommand.Dispose();

        var transferredCommand = StreamCommand();
        await foreach (string _ in varyingType.StreamQueryAsync<string>(transferredCommand, disposeCommand: true, ct: ct)) { }
        Assert.Equal(1, transferredCommand.DisposeCount);
    }
}
