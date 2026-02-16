using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// A cached result containing the compiled delegate and the schema context used to generate it.
/// </summary>
/// <param name="dm">The <see cref="DynamicMethod"/> that maps a row to <typeparamref name="T"/>.</param>
/// <param name="cols">The specific schema this parser is built for.</param>
/// <param name="behavior">The most optimal <see cref="CommandBehavior"/> for this specific parser.</param>
public unsafe readonly struct DbParsingInfo<T>(DynamicMethod dm, ColumnInfo[] cols, CommandBehavior behavior) {
    /// <summary>The actual function that parse the row</summary>
    public readonly Func<DbDataReader, T> ReaderFunc = dm.CreateDelegate<Func<DbDataReader, T>>();

    /// <summary>The specific schema this parser is built for.</summary>
    public readonly ColumnInfo[] Schema = cols;
    /// <summary>The most optimal <see cref="CommandBehavior"/> for this specific parser.</summary>
    public readonly CommandBehavior DefaultBehavior = behavior;
}
/// <summary>A simple struct used track the usage of the columns</summary>
public readonly ref struct ColumnUsage(Span<bool> Span) {
    private readonly Span<bool> Span = Span;
    /// <summary>The amount of columns</summary>
    public readonly int Length => Span.Length;
    /// <summary>
    /// Save a snapshot of the current usage into a checkpoint <see cref="Span{Boolean}"/>
    /// </summary>
    public readonly void InitCheckpoint(Span<bool> checkpoint) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            checkpoint[i] = Span[i];
    }
    /// <summary>
    /// Reset the column usage to the checkpoint state
    /// </summary>
    public readonly void Rollback(Span<bool> checkpoint) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            Span[i] = checkpoint[i];
    }
    /// <summary>
    /// Check if a column has been marked as used
    /// </summary>
    public readonly bool IsUsed(int ind) => Span[ind];
    /// <summary>
    /// Mark a column as used
    /// </summary>
    public readonly void Use(int ind) => Span[ind] = true;
}
/// <summary>
/// Manages the generation and caching of specialized parsers for <typeparamref name="T"/>.
/// </summary>
public static class TypeParser<T> {
    private static readonly List<DbParsingInfo<T>> ReadingInfos = [];

    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="nullColHandler">Specified nullability handling</param>
    public unsafe static Func<DbDataReader, T> GetParserFunc(ref ColumnInfo[] cols, INullColHandler? nullColHandler = null)
        => GetParserFunc(ref cols, out _, nullColHandler);
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="isNullable">Identify wether to throw or not when the root item is null</param>
    public unsafe static Func<DbDataReader, T> GetParserFunc(ref ColumnInfo[] cols, bool isNullable)
        => GetParserFunc(ref cols, out _, isNullable ? NullableTypeHandle.Instance : NotNullHandle.Instance);
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="isNullable">Identify wether to throw or not when the root item is null</param>
    /// <param name="defaultBehavior">Outputs the optimized behavior (e.g., SequentialAccess).</param>
    public unsafe static Func<DbDataReader, T> GetParserFunc(ref ColumnInfo[] cols, bool isNullable, out CommandBehavior defaultBehavior)
        => GetParserFunc(ref cols, out defaultBehavior, isNullable ? NullableTypeHandle.Instance : NotNullHandle.Instance);
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="defaultBehavior">Outputs the optimized behavior (e.g., SequentialAccess).</param>
    /// <param name="nullColHandler">Specified nullability handling</param>
    public unsafe static Func<DbDataReader, T> GetParserFunc(ref ColumnInfo[] cols, out CommandBehavior defaultBehavior, INullColHandler? nullColHandler = null) {
        for (int i = 0; i < ReadingInfos.Count; i++) {
            if (cols.Equal(ReadingInfos[i].Schema)) {
                var rdInfo = ReadingInfos[i];
                cols = rdInfo.Schema;
                defaultBehavior = rdInfo.DefaultBehavior;
                return rdInfo.ReaderFunc;
            }
        }
        if (!TryMakeParser(typeof(T), nullColHandler, cols, out var info))
            throw new Exception($"cannot make the parser for {typeof(T)} with the schema ({string.Join(", ", cols.Select(c => $"{c.Type.Name}{(c.IsNullable ? "?" : "")} {c.Name}"))})");
        ReadingInfos.Add(info);
        defaultBehavior = info.DefaultBehavior;
        return info.ReaderFunc;
    }
    private static readonly Type[] TReaderArg = [typeof(DbDataReader)];
    internal static readonly Module Module = typeof(DbDataReader).Module;
    /// <summary>
    /// The compilation core. Orchestrates the transition from metadata to IL.
    /// </summary>
    /// <remarks>
    /// <b>Process:</b>
    /// <list type="number">
    /// <item>Determines the appropriate <see cref="INullColHandler"/> based on the nullability of <typeparamref name="T"/>.</item>
    /// <item>Requests a <see cref="DbItemParser"/> (emission tree) from the <see cref="TypeParsingInfo"/> registry.</item>
    /// <item>Initializes a <see cref="DynamicMethod"/> and uses the emission tree to generate IL via <see cref="Generator"/>.</item>
    /// <item>Evaluates if the generated logic allows for <see cref="CommandBehavior.SequentialAccess"/> optimization.</item>
    /// </list>
    /// </remarks>
    private unsafe static bool TryMakeParser(Type closedType, INullColHandler? nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out DbParsingInfo<T> parser) {
        bool isNullable = Nullable.GetUnderlyingType(typeof(T)) is not null;
        nullColHandler ??= isNullable ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        var colUsage = new ColumnUsage(stackalloc bool[cols.Length]);
        var rd = TypeParsingInfo.ForceGet(closedType)
            .TryGetParser(closedType.IsGenericType ? closedType.GetGenericArguments() : [], "root", nullColHandler, cols, new(), isNullable, ref colUsage);
        if (rd is null) {
            parser = default;
            return false;
        }
        var dm = new DynamicMethod(
            $"Map_{typeof(T).Name}_{Guid.NewGuid():N}",
            typeof(T), TReaderArg, Module,
            skipVisibility: true
        );
        Generator gen =
#if DEBUG
            new(dm.GetILGenerator(), cols);
#else
            new(dm.GetILGenerator());
#endif
        rd.Emit(cols, gen, rd.NeedNullSetPoint(cols) ? new(gen.DefineLabel(), 0) : default);
        gen.Emit(OpCodes.Ret);
        dm.DefineParameter(1, ParameterAttributes.In, "reader");
        var prevIndex = -1;
        var defaultBehavior = cols.Length == 1 ? CommandBehavior.SingleResult : CommandBehavior.Default;
        if (rd.IsSequencial(ref prevIndex))
            defaultBehavior |= CommandBehavior.SequentialAccess;
        parser = new(dm, cols, defaultBehavior);
        return true;
    }
}