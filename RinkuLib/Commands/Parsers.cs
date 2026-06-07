using System.Data;
using System.Data.Common;
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
        static (Type baseType, bool isNullable) GetNormalizedInfo(Type type, NullabilityInfo nullInfo) {
            Type? underlying = Nullable.GetUnderlyingType(type);
            bool isNullable = underlying != null || nullInfo.WriteState == NullabilityState.Nullable;
            return (underlying ?? type, isNullable);
        }

        var ctor = FindBestConstructor(type);
        if (ctor != null) {
            foreach (var param in ctor.GetParameters()) {
                string name = INameComparer.TryGetTrueName(param, out var trueName) ? trueName : param.Name!;
                var (baseType, isNullable) = GetNormalizedInfo(param.ParameterType, nullabilityContext.Create(param));
                columns.Add(new ColumnInfo(name, baseType, isNullable));
            }
            return [.. columns];
        }
        
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
            if (member is PropertyInfo prop) {
                string name = INameComparer.TryGetTrueName(prop, out var trueName) ? trueName : prop.Name;
                var (baseType, isNullable) = GetNormalizedInfo(prop.PropertyType, nullabilityContext.Create(prop));
                columns.Add(new ColumnInfo(name, baseType, isNullable));
            }
            else if (member is FieldInfo field) {
                string name = INameComparer.TryGetTrueName(field, out var trueName) ? trueName : field.Name;
                var (baseType, isNullable) = GetNormalizedInfo(field.FieldType, nullabilityContext.Create(field));
                columns.Add(new ColumnInfo(name, baseType, isNullable));
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
    /// <summary></summary>
    public static readonly ITypeParser<T> Parser;
}
/// <summary></summary>
public static class ParsersExtensions {
    /// <summary>
    /// Executes the <see cref="DbCommand"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this DbCommand command, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, disposeCommand);

    /// <summary>
    /// Executes the <see cref="IDbCommand"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this IDbCommand command, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, disposeCommand);

    /// <summary>
    /// Executes the <see cref="DbCommand"/> with an <see cref="ICache"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cache">The cache to be used with the reader.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this DbCommand command, ICache cache, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, cache, disposeCommand);

    /// <summary>
    /// Executes the <see cref="IDbCommand"/> with an <see cref="ICache"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cache">The cache to be used with the reader.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Query<TSchema, T>(this IDbCommand command, ICache cache, bool disposeCommand = false)
        => Parsers<TSchema, T>.Parser.Query(command, cache, disposeCommand);

    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    /// <param name="ct">The cancellation token.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this DbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, disposeCommand, ct);

    /// <summary>
    /// Asynchronously executes the <see cref="IDbCommand"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    /// <param name="ct">The cancellation token.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this IDbCommand command, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, disposeCommand, ct);

    /// <summary>
    /// Asynchronously executes the <see cref="DbCommand"/> with an <see cref="ICache"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cache">The cache to be used with the reader.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    /// <param name="ct">The cancellation token.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this DbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, cache, disposeCommand, ct);

    /// <summary>
    /// Asynchronously executes the <see cref="IDbCommand"/> with an <see cref="ICache"/> and projects the result set into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="TSchema">The type defining the database schema metadata.</typeparam>
    /// <typeparam name="T">The destination type to be returned.</typeparam>
    /// <param name="command">The command to execute.</param>
    /// <param name="cache">The cache to be used with the reader.</param>
    /// <param name="disposeCommand">If <c>true</c>, the command is disposed after execution.</param>
    /// <param name="ct">The cancellation token.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<T> QueryAsync<TSchema, T>(this IDbCommand command, ICache cache, bool disposeCommand = false, CancellationToken ct = default)
        => Parsers<TSchema, T>.Parser.QueryAsync(command, cache, disposeCommand, ct);
}