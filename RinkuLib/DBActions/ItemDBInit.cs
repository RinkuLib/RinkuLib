using System.Data;
using System.Data.Common;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.DBActions;
/// <summary></summary>
public readonly record struct QueryCommandUsingObjectParam(QueryCommand Command, DbCommand Cmd, object? ParameterObj = null) : IParserGetter {
    /// <summary></summary>
    public static SchemaParser<TParsed> UpdateCache<TParsed>(QueryCommand command, ColumnInfo[] schema, bool[] usageMap) {
        var p = TypeParser<TParsed>.GetParserFunc(ref schema, out var def);
        var parser = new SchemaParser<TParsed>(p, def);
        command.UpdateParseCache(usageMap, schema, parser);
        return parser;
    }
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser))
            return Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        if (parser.parser is null)
            parser = UpdateCache<TParsed>(Command, reader.GetColumns(), usageMap.ToArray());
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser)) { }
        else if (parser.parser is not null)
            Command.UpdateCache(Cmd);
        else {
            var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            parser = UpdateCache<TParsed>(Command, reader.GetColumns(), usageMap.ToArray());
            return Task.FromResult(reader);
        }
        return Cmd.ExecuteReaderAsync(parser.Behavior | defaultBehavior, ct);
    }
}
/// <summary></summary>
public readonly record struct QueryCommandUsingObjectParam<TObj>(QueryCommand Command, DbCommand Cmd, TObj ParameterObj) : IParserGetter where TObj : notnull {
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser))
            return Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        if (parser.parser is null)
            parser = QueryCommandUsingObjectParam.UpdateCache<TParsed>(Command, reader.GetColumns(), usageMap.ToArray());
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser)) { }
        else if (parser.parser is not null)
            Command.UpdateCache(Cmd);
        else {
            var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            parser = QueryCommandUsingObjectParam.UpdateCache<TParsed>(Command, reader.GetColumns(), usageMap.ToArray());
            return Task.FromResult(reader);
        }
        return Cmd.ExecuteReaderAsync(parser.Behavior | defaultBehavior, ct);
    }
}
/// <summary></summary>
public readonly record struct QueryCommandUsingObjectParamLegacy(QueryCommand Command, IDbCommand Cmd, object? ParameterObj = null) : IParserGetter {
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser)) {
            var r = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            return r is DbDataReader rd ? rd : new WrappedBasicReader(r);
        }
        var rdd = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = rdd is DbDataReader rrd ? rrd : new WrappedBasicReader(rdd);
        if (parser.parser is null) {
            var schema = reader.GetColumns();
            var p = TypeParser<TParsed>.GetParserFunc(ref schema, out var def);
            parser = new(p, def);
            Command.UpdateParseCache(usageMap.ToArray(), schema, parser);
        }
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) => Task.FromResult(GetParserAndReader(defaultBehavior, out parser));
}
/// <summary></summary>
public readonly record struct QueryCommandUsingObjectParamLegacy<TObj>(QueryCommand Command, IDbCommand Cmd, TObj ParameterObj) : IParserGetter where TObj : notnull {
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Span<bool> usageMap = stackalloc bool[Command.Mapper.Count];
        Command.SetCommand(Cmd, ParameterObj, usageMap);
        if (Command.TryGetCache(usageMap, out parser)) {
            var r = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            return r is DbDataReader rd ? rd : new WrappedBasicReader(r);
        }
        var rdd = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = rdd is DbDataReader rrd ? rrd : new WrappedBasicReader(rdd);
        if (parser.parser is null) {
            var schema = reader.GetColumns();
            var p = TypeParser<TParsed>.GetParserFunc(ref schema, out var def);
            parser = new(p, def);
            Command.UpdateParseCache(usageMap.ToArray(), schema, parser);
        }
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) => Task.FromResult(GetParserAndReader(defaultBehavior, out parser));
}
/// <summary></summary>
public readonly record struct QueryCommandBuilderCommand(QueryCommand Command, DbCommand Cmd, object?[] Variables) : IParserGetter {
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Command.SetCommand(Cmd, Variables);
        if (Command.TryGetCache(Variables, out parser))
            return Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        if (parser.parser is null)
            parser = QueryCommandUsingObjectParam.UpdateCache<TParsed>(Command, reader.GetColumns(), Variables.ToBoolArray());
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) {
        Command.SetCommand(Cmd, Variables);
        if (Command.TryGetCache(Variables, out parser)) { }
        else if (parser.parser is not null)
            Command.UpdateCache(Cmd);
        else {
            var reader = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            parser = QueryCommandUsingObjectParam.UpdateCache<TParsed>(Command, reader.GetColumns(), Variables.ToBoolArray());
            return Task.FromResult(reader);
        }
        return Cmd.ExecuteReaderAsync(parser.Behavior | defaultBehavior, ct);
    }
}
/// <summary></summary>
public readonly record struct QueryCommandBuilderCommandLegacy(QueryCommand Command, IDbCommand Cmd, object?[] Variables) : IParserGetter {
    /// <inheritdoc/>
    public readonly DbDataReader GetParserAndReader<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser) {
        Command.SetCommand(Cmd, Variables);
        if (Command.TryGetCache(Variables, out parser)) {
            var r = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
            return r is DbDataReader rd ? rd : new WrappedBasicReader(r);
        }
        var rdd = Cmd.ExecuteReader(parser.Behavior | defaultBehavior);
        var reader = rdd is DbDataReader rrd ? rrd : new WrappedBasicReader(rdd);
        if (parser.parser is null) {
            var schema = reader.GetColumns();
            var p = TypeParser<TParsed>.GetParserFunc(ref schema, out var def);
            parser = new(p, def);
            Command.UpdateParseCache(Variables.ToBoolArray(), schema, parser);
        }
        Command.UpdateCache(Cmd);
        return reader;
    }
    /// <inheritdoc/>
    public readonly Task<DbDataReader> GetParserAndReaderAsync<TParsed>(CommandBehavior defaultBehavior, out SchemaParser<TParsed> parser, CancellationToken ct = default) => Task.FromResult(GetParserAndReader(defaultBehavior, out parser));
}