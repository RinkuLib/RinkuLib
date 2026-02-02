using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

namespace RinkuLib.DbParsing;
/// <summary>
/// A cached result containing the compiled delegate and the schema context used to generate it.
/// </summary>
/// <param name="Reader">The compiled IL delegate that maps a row to <typeparamref name="T"/>.</param>
/// <param name="SelectColumns">The specific schema (column order, types, names) this reader is built for.</param>
/// <param name="DefaultBehavior">The recommended <see cref="CommandBehavior"/> for this specific parser.</param>
public readonly record struct DbParsingInfo<T>(Func<DbDataReader, T> Reader, ColumnInfo[] SelectColumns, CommandBehavior DefaultBehavior);
public static class Helper {
    /// <summary>
    /// Extracts the schema from a <see cref="DbDataReader"/> and converts it into a 
    /// <see cref="ColumnInfo"/> array for the Negotiation Phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColumnInfo[] GetColumns(this DbDataReader reader)
        => reader.GetColumnSchema().GetColumns();

    public static ColumnInfo[] GetColumns(this ReadOnlyCollection<DbColumn> schema) {
        var columns = new ColumnInfo[schema.Count];
        for (var i = 0; i < schema.Count; i++) {
            var column = schema[i];
            string name = column.ColumnName ?? string.Empty;
            if (column.IsAliased == false && column.IsExpression == true)
                name = string.Empty;
            columns[i] = new ColumnInfo {
                Name = name,
                Type = column.DataType ?? typeof(object),
                IsNullable = column.AllowDBNull ?? true
            };
        }
        return columns;
    }
    /// <summary>
    /// Compares two schema arrays for structural equality. 
    /// This is used to determine if a cached parser can be reused for a new request.
    /// </summary>
    public static bool Equals(this ColumnInfo[] cols1, ColumnInfo[] cols2) {
        if (cols1.Length != cols2.Length)
            return false;
        for (var i = 0; i < cols1.Length; i++)
            if (!cols1[i].Equals(cols2[i]))
                return false;
        return true;
    }
}
public readonly ref struct ColumnUsage(Span<bool> Span) {
    public readonly Span<bool> Span = Span;
    public readonly int Length => Span.Length;
    public readonly void InitCheckpoint(Span<bool> checkpoint) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            checkpoint[i] = Span[i];
    }
    public readonly void Rollback(Span<bool> checkpoint) {
        if (checkpoint.Length != Span.Length)
            throw new Exception($"must be the same length expected:{Span.Length} actual:{checkpoint.Length}");
        for (var i = 0; i < Span.Length; i++)
            Span[i] = checkpoint[i];
    }
    public readonly bool IsUsed(int ind) => Span[ind];
    public readonly void Use(int ind) => Span[ind] = true;
}
/// <summary>
/// Manages the generation and caching of specialized parsers for <typeparamref name="T"/>.
/// </summary>
public static class TypeParser<T> {
    private static readonly List<DbParsingInfo<T>> ReadingInfos = [];

    public unsafe static Func<DbDataReader, T> GetParser(ColumnInfo[] cols, INullColHandler? nullColHandler = null)
        => GetParser(cols, out _, nullColHandler);
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="defaultBehavior">Outputs the optimized behavior (e.g., SequentialAccess).</param>
    /// <param name="parser">The compiled function to execute.</param>
    /// <param name="nullColHandler">Support for nullability handling</param>
    public unsafe static Func<DbDataReader, T> GetParser(ColumnInfo[] cols, out CommandBehavior defaultBehavior, INullColHandler? nullColHandler = null) {
        for (int i = 0; i < ReadingInfos.Count; i++) {
            if (Helper.Equals(ReadingInfos[i].SelectColumns, cols)) {
                defaultBehavior = ReadingInfos[i].DefaultBehavior;
                return ReadingInfos[i].Reader;
            }
        }
        if (!TryMakeParser(typeof(T), nullColHandler, cols, out var info))
            throw new Exception($"cannot make the parser for {typeof(T)} with the schema ({string.Join(", ", cols.Select(c => $"{c.Type.Name}{(c.IsNullable ? "?" : "")} {c.Name}"))})");
        ReadingInfos.Add(info);
        defaultBehavior = info.DefaultBehavior;
        return info.Reader;
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
    private static bool TryMakeParser(Type closedType, INullColHandler? nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out DbParsingInfo<T> parser) {
        bool isNullable = Nullable.GetUnderlyingType(typeof(T)) is not null;
        nullColHandler ??= isNullable ? NullableTypeHandle.Instance : NotNullHandle.Instance;
        var colUsage = new ColumnUsage(stackalloc bool[cols.Length]);
        var rd = TypeParsingInfo.GetOrAdd(closedType)
            .TryGetParser(closedType.IsGenericType ? closedType.GetGenericArguments() : [], nullColHandler, cols, new(), isNullable, ref colUsage);
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
#if USE_VERBOSE_GENERATOR
            new(dm.GetILGenerator(), cols);
#else
            new(dm.GetILGenerator());
#endif
        rd.Emit(cols, gen, rd.NeedNullSetPoint(cols) ? new(gen.DefineLabel(), 0) : default);
        gen.Emit(OpCodes.Ret);
        var r = (Func<DbDataReader, T>)dm.CreateDelegate(typeof(Func<DbDataReader, T>));
        var prevIndex = -1;
        var defaultBehiavor = cols.Length == 1 ? CommandBehavior.SingleResult : CommandBehavior.Default;
        if (rd.IsSequencial(ref prevIndex))
            defaultBehiavor |= CommandBehavior.SequentialAccess;
        parser = new(r, cols, defaultBehiavor);
        return true;
    }
}