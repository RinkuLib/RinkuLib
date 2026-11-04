using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Rinku;

public static partial class DatabaseCommandExtensions
{
    public static int Execute(this IDatabase database, Func<DbConnection, DbCommand> factory, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state(connection);
        return command.ExecuteNonQuery();
    }, factory, DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync(this IDatabase database, Func<DbConnection, DbCommand> factory, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state(connection);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, factory, DatabaseExtensions.Source(factory, source), cancellationToken);

    public static TResult Query<TResult>(this IDatabase database, Func<DbConnection, DbCommand> factory, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state(connection);
        return command.Query<TResult>(disposeCommand: false);
    }, factory, DatabaseExtensions.Source(factory, source));

    public static Task<TResult> QueryAsync<TResult>(this IDatabase database, Func<DbConnection, DbCommand> factory, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state(connection);
        return await command.QueryAsync<TResult>(disposeCommand: false, ct: token).ConfigureAwait(false);
    }, factory, DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1>(this IDatabase database, Func<DbConnection, T1, DbCommand> factory, T1 argument1, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: argument1), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1>(this IDatabase database, Func<DbConnection, T1, DbCommand> factory, T1 argument1, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: argument1), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static TResult Query<TResult, T1>(this IDatabase database, Func<DbConnection, T1, DbCommand> factory, T1 argument1, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments);
        return command.Query<TResult>(disposeCommand: false);
    }, (Factory: factory, Arguments: argument1), DatabaseExtensions.Source(factory, source));

    public static Task<TResult> QueryAsync<TResult, T1>(this IDatabase database, Func<DbConnection, T1, DbCommand> factory, T1 argument1, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments);
        return await command.QueryAsync<TResult>(disposeCommand: false, ct: token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: argument1), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2>(this IDatabase database, Func<DbConnection, T1, T2, DbCommand> factory, T1 argument1, T2 argument2, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2>(this IDatabase database, Func<DbConnection, T1, T2, DbCommand> factory, T1 argument1, T2 argument2, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3>(this IDatabase database, Func<DbConnection, T1, T2, T3, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3>(this IDatabase database, Func<DbConnection, T1, T2, T3, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3, T4>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3, T4>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3, T4, T5>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3, T4, T5>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3, T4, T5, T6>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3, T4, T5, T6>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3, T4, T5, T6, T7>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3, T4, T5, T6, T7>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), DatabaseExtensions.Source(factory, source), cancellationToken);

    public static int Execute<T1, T2, T3, T4, T5, T6, T7, T8>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, [CallerArgumentExpression(nameof(factory))] string source = "") => database.Execute(static (connection, state) =>
    {
        using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8);
        return command.ExecuteNonQuery();
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), DatabaseExtensions.Source(factory, source));

    public static Task<int> ExecuteAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8, DbCommand> factory, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(factory))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await using var command = state.Factory(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, (Factory: factory, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), DatabaseExtensions.Source(factory, source), cancellationToken);
}

