using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

/// <summary>
/// Opens a builder over a <see cref="QueryCommand"/>. A builder lets code set the query's values and switch
/// its conditional parts on or off across several steps before running, in place of passing one parameter
/// object. Take this road when the values come from branching logic.
/// </summary>
public static class BuilderStarter {
    /// <summary>
    /// Opens a builder that holds its values in memory and builds a command only when it runs.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command)
        => new(command);
    /// <summary>
    /// Opens a builder bound to a <see cref="DbCommand"/> you own, so each value set flows onto that command
    /// at once. Reuse the command to run the query many times without rebuilding it.
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd)
        => new(command, cmd);
    /// <inheritdoc cref="StartBuilder(QueryCommand, DbCommand)"/>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd)
        => new(command, cmd);
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
        var builder = new QueryBuilderCommand<DbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <inheritdoc cref="StartBuilder(QueryCommand, DbCommand, Span{ValueTuple{string, object}})"/>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd, params Span<(string, object)> values) {
        var builder = new QueryBuilderCommand<IDbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
}
