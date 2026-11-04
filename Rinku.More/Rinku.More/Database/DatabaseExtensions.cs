using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Rinku;

public static class DatabaseExtensions
{
    public static void Execute(this IDatabase database, Action<DbConnection> operation, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state(connection);
        return true;
    }, operation, Source(operation, source));

    public static TResult Execute<TResult>(this IDatabase database, Func<DbConnection, TResult> operation, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state(connection), operation, Source(operation, source));

    public static Task ExecuteAsync(this IDatabase database, Func<DbConnection, CancellationToken, Task> operation, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state(connection, token).ConfigureAwait(false);
        return true;
    }, operation, Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<TResult>(this IDatabase database, Func<DbConnection, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state(connection, token), operation, Source(operation, source), cancellationToken);

    public static void Execute<T1>(this IDatabase database, Action<DbConnection, T1> operation, T1 argument1, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments);
        return true;
    }, (Operation: operation, Arguments: argument1), Source(operation, source));

    public static TResult Execute<T1, TResult>(this IDatabase database, Func<DbConnection, T1, TResult> operation, T1 argument1, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments), (Operation: operation, Arguments: argument1), Source(operation, source));

    public static Task ExecuteAsync<T1>(this IDatabase database, Func<DbConnection, T1, CancellationToken, Task> operation, T1 argument1, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: argument1), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, TResult>(this IDatabase database, Func<DbConnection, T1, CancellationToken, Task<TResult>> operation, T1 argument1, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments, token), (Operation: operation, Arguments: argument1), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2>(this IDatabase database, Action<DbConnection, T1, T2> operation, T1 argument1, T2 argument2, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2)), Source(operation, source));

    public static TResult Execute<T1, T2, TResult>(this IDatabase database, Func<DbConnection, T1, T2, TResult> operation, T1 argument1, T2 argument2, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2), (Operation: operation, Arguments: (argument1, argument2)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2>(this IDatabase database, Func<DbConnection, T1, T2, CancellationToken, Task> operation, T1 argument1, T2 argument2, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, TResult>(this IDatabase database, Func<DbConnection, T1, T2, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, token), (Operation: operation, Arguments: (argument1, argument2)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3>(this IDatabase database, Action<DbConnection, T1, T2, T3> operation, T1 argument1, T2 argument2, T3 argument3, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, TResult> operation, T1 argument1, T2 argument2, T3 argument3, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3), (Operation: operation, Arguments: (argument1, argument2, argument3)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3>(this IDatabase database, Func<DbConnection, T1, T2, T3, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, token), (Operation: operation, Arguments: (argument1, argument2, argument3)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3, T4>(this IDatabase database, Action<DbConnection, T1, T2, T3, T4> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, T4, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, TResult> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3, T4>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, T4, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, token), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3, T4, T5>(this IDatabase database, Action<DbConnection, T1, T2, T3, T4, T5> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, T4, T5, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, TResult> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3, T4, T5>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, T4, T5, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, token), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3, T4, T5, T6>(this IDatabase database, Action<DbConnection, T1, T2, T3, T4, T5, T6> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, T4, T5, T6, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, TResult> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3, T4, T5, T6>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, T4, T5, T6, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, token), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3, T4, T5, T6, T7>(this IDatabase database, Action<DbConnection, T1, T2, T3, T4, T5, T6, T7> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, T4, T5, T6, T7, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, TResult> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3, T4, T5, T6, T7>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, T4, T5, T6, T7, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, token), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7)), Source(operation, source), cancellationToken);

    public static void Execute<T1, T2, T3, T4, T5, T6, T7, T8>(this IDatabase database, Action<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) =>
    {
        state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), Source(operation, source));

    public static TResult Execute<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8, TResult> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, [CallerArgumentExpression(nameof(operation))] string source = "") => database.Execute(static (connection, state) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), Source(operation, source));

    public static Task ExecuteAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8, CancellationToken, Task> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static async (connection, state, token) =>
    {
        await state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8, token).ConfigureAwait(false);
        return true;
    }, (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), Source(operation, source), cancellationToken);

    public static Task<TResult> ExecuteAsync<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this IDatabase database, Func<DbConnection, T1, T2, T3, T4, T5, T6, T7, T8, CancellationToken, Task<TResult>> operation, T1 argument1, T2 argument2, T3 argument3, T4 argument4, T5 argument5, T6 argument6, T7 argument7, T8 argument8, CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(operation))] string source = "") => database.ExecuteAsync(static (connection, state, token) => state.Operation(connection, state.Arguments.Item1, state.Arguments.Item2, state.Arguments.Item3, state.Arguments.Item4, state.Arguments.Item5, state.Arguments.Item6, state.Arguments.Item7, state.Arguments.Item8, token), (Operation: operation, Arguments: (argument1, argument2, argument3, argument4, argument5, argument6, argument7, argument8)), Source(operation, source), cancellationToken);

    internal static string Source(Delegate operation, string source) => source.Length == 0 ? operation.Method.Name : source;
}

