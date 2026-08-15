using System.Data;
using System.Data.Common;
using Rinku.Querying;

namespace Rinku;

/// <summary>
/// Opens a builder for a <see cref="QueryCommand"/>. Use a builder when values are supplied in several steps
/// or when code needs to choose which optional parts of the query are used.
/// </summary>
public static class BuilderStarter {
    /// <summary>
    /// Opens a builder that collects values until the query runs.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command)
        => new(command);
    /// <summary>
    /// Opens a builder that writes values to a <see cref="DbCommand"/> supplied by the caller.
    /// Use this overload when the same database command will run more than once.
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd) {
        command.EnsureReturnValueParameter(cmd);
        return new(command, cmd);
    }
    /// <inheritdoc cref="StartBuilder(QueryCommand, DbCommand)"/>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd) {
        command.EnsureReturnValueParameter(cmd);
        return new(command, cmd);
    }
    /// <summary>
    /// Opens an in-memory builder already seeded with the given name and value pairs.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command, params Span<(string, object)> values) {
        var builder = new QueryBuilder(command);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <summary>
    /// Opens a builder bound to the given <see cref="DbCommand"/>, already seeded with the name and value pairs.
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd, params Span<(string, object)> values) {
        command.EnsureReturnValueParameter(cmd);
        var builder = new QueryBuilderCommand<DbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <inheritdoc cref="StartBuilder(QueryCommand, DbCommand, Span{ValueTuple{string, object}})"/>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd, params Span<(string, object)> values) {
        command.EnsureReturnValueParameter(cmd);
        var builder = new QueryBuilderCommand<IDbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
}
