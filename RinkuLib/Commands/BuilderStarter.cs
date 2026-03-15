using System.Data;
using System.Data.Common;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

/// <summary>
/// Provides extension methods to start and/or populate a <see cref="IQueryBuilder"/> 
/// </summary>
public static class BuilderStarter {
    /// <summary>
    /// Start a <see cref="QueryBuilder"/>.
    /// </summary>
    public static QueryBuilder StartBuilder(this QueryCommand command)
        => new(command);
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/>.
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd)
        => new(command, cmd);
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/>.
    /// </summary>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd)
        => new(command, cmd);
    /// <summary>
    /// Start a <see cref="QueryBuilder"/> and set usage with the <paramref name="values"/>
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
    /// Start a <see cref="QueryBuilderCommand{T}"/> and set usage with the <paramref name="values"/>
    /// </summary>
    public static QueryBuilderCommand<DbCommand> StartBuilder(this QueryCommand command, DbCommand cmd, params Span<(string, object)> values) {
        var builder = new QueryBuilderCommand<DbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
    /// <summary>
    /// Start a <see cref="QueryBuilderCommand{T}"/> and set usage with the <paramref name="values"/>
    /// </summary>
    public static QueryBuilderCommand<IDbCommand> StartBuilder(this QueryCommand command, IDbCommand cmd, params Span<(string, object)> values) {
        var builder = new QueryBuilderCommand<IDbCommand>(command, cmd);
        for (int i = 0; i < values.Length; i++) {
            var (key, value) = values[i];
            builder.Use(key, value);
        }
        return builder;
    }
}
