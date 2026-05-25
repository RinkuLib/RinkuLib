using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Commands;
/// <summary>
/// Provides a mechanism to extract schema definitions from types.
/// </summary>
public interface ISchemaProvider {
    /// <summary>
    /// Returns the schema definition for the implementing type.
    /// </summary>
    static abstract ColumnInfo[] GetSchema();
}
/// <summary>
/// A registry for schema metadata extraction.
/// </summary>
/// <typeparam name="T">The type to extract schema from.</typeparam>
public static class TypeSchema<T> {
    /// <summary>
    /// The extracted schema for <typeparamref name="T"/>.
    /// </summary>
    public static readonly ColumnInfo[] Schema = ExtractColumns();

    private static ColumnInfo[] ExtractColumns() {
        var type = typeof(T);

        if (typeof(ISchemaProvider).IsAssignableFrom(type)) {
            var method = type.GetMethod(nameof(ISchemaProvider.GetSchema),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (method?.Invoke(null, null) is ColumnInfo[] customSchema)
                return customSchema;
        }

        var columns = new List<ColumnInfo>();
        var nullabilityContext = new NullabilityInfoContext();

        var ctor = FindBestConstructor(type);
        if (ctor != null) {
            foreach (var param in ctor.GetParameters()) {
                string name = INameComparer.TryGetTrueName(param, out var trueName) ? trueName : param.Name!;
                var nullInfo = nullabilityContext.Create(param);
                columns.Add(new ColumnInfo(name, param.ParameterType, nullInfo.WriteState == NullabilityState.Nullable));
            }
            return [.. columns];
        }
        
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
            if (member is PropertyInfo prop) {
                string name = INameComparer.TryGetTrueName(prop, out var trueName) ? trueName : prop.Name;
                var nullInfo = nullabilityContext.Create(prop);
                columns.Add(new ColumnInfo(name, prop.PropertyType, nullInfo.WriteState == NullabilityState.Nullable));
            }
            else if (member is FieldInfo field) {
                string name = INameComparer.TryGetTrueName(field, out var trueName) ? trueName : field.Name;
                var nullInfo = nullabilityContext.Create(field);
                columns.Add(new ColumnInfo(name, field.FieldType, nullInfo.WriteState == NullabilityState.Nullable));
            }
        }

        return [.. columns];
    }

    private static ConstructorInfo? FindBestConstructor(Type type) {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        foreach (var ctor in ctors) {
            if (ctor.GetParameters().Length > 0)
                return ctor;
        }
        return null;
    }
}
/// <summary></summary>
public static class Parsers<TSchema, T> {
    static Parsers() {
        var schema = TypeSchema<TSchema>.Schema;
        Parser = TypeParser<T>.GetTypeParser(ref schema);
    }
    internal static readonly ITypeParser<T> Parser;
}
/// <summary></summary>
public static class ParsersExtensions {
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this DbCommand command, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, disposeCommand);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this IDbCommand command, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, disposeCommand);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="cache">A cache to be used with the reader</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this DbCommand command, ICache cache, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, cache, disposeCommand);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="cache">A cache to be used with the reader</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this IDbCommand command, ICache cache, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, cache, disposeCommand);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    /// <param name="ct">The fowarded cancellation token</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this DbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, disposeCommand, ct);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    /// <param name="ct">The fowarded cancellation token</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this IDbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, disposeCommand, ct);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="cache">A cache to be used with the reader</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    /// <param name="ct">The fowarded cancellation token</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, cache, disposeCommand, ct);
    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and parse the first row to return an instance of <typeparamref name="T"/> or the default if no result.
    /// </summary>
    /// <param name="command">The command to execute the query on</param>
    /// <param name="cache">A cache to be used with the reader</param>
    /// <param name="disposeCommand">Indicate if the command should be properly disposed after execution</param>
    /// <param name="ct">The fowarded cancellation token</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, cache, disposeCommand, ct);
}
