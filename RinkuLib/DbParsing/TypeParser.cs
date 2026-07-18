using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Commands;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary>
/// Builds the parser that reads a result into a given <c>T</c>, and caches it by schema. This is where a
/// result shape is chosen, the makers in <see cref="TypeParserMakers"/> are tried in order and the first to
/// claim <c>T</c> builds it. Add your own maker to that list to teach the engine a new shape.
/// </summary>
public static class TypeParser {
    /// <summary>
    /// The cache for parsers requested and root nullability. Copy-on-write so readers scan lock-free
    /// while writers swap in a grown array under <see cref="DefaultTypeParsingInfo.WriteLock"/>.
    /// </summary>
    internal static (ColumnInfo[] Schema, INullColHandler NullColHandler, object Parser)[] ReadingInfos = [];
    /// <summary>The fallback maker, the object parser, that claims any <c>T</c> no other maker did.</summary>
    public static readonly DefaultTypeParserMaker DefaultTypeParserMaker = new();
    /// <summary>
    /// The makers tried in order to build a parser for a <c>T</c>, the built-in result shapes among them.
    /// Insert your own ahead of the defaults to add a shape (see the parsers guide).
    /// </summary>
    public static readonly List<ITypeParserMaker> TypeParserMakers = [
        new ReusingBaseTypeParserMaker([typeof(IEnumerable<>), typeof(List<>)],
            (def, itemType, ref _) => (def == typeof(IEnumerable<>))
                ? typeof(EnumerableTypeParser<>).MakeGenericType(itemType)
                : typeof(ListTypeParser<>).MakeGenericType(itemType),
            (def, itemType, ref _) => (def == typeof(IEnumerable<>))
                ? typeof(FastEnumerableTypeParser<>).MakeGenericType(itemType)
                : typeof(FastListTypeParser<>).MakeGenericType(itemType)
        ),
        new ReusingBaseTypeParserMaker([typeof(Optional<>), typeof(OptionalStruct<>), typeof(OptionalNullable<>)],
            (def, itemType, ref _) => typeof(OptionalTypeParser<,>).MakeGenericType(def.MakeGenericType(itemType), itemType),
            (def, itemType, ref _) => typeof(FastOptionalTypeParser<,>).MakeGenericType(def.MakeGenericType(itemType), itemType)
        ),
        new ReusingBaseTypeParserMaker([typeof(Single<>)],
            (def, itemType, ref _) => typeof(SingleTypeParser<,>).MakeGenericType(def.MakeGenericType(itemType), itemType),
            (def, itemType, ref _) => typeof(FastSingleTypeParser<,>).MakeGenericType(def.MakeGenericType(itemType), itemType)
        )
    ];
    /// <summary>The root nullability implied by <typeparamref name="T"/> itself</summary>
    public static INullColHandler GetDefaultNullColHandler<T>() => Nullable.GetUnderlyingType(typeof(T)) is not null
        ? NullableTypeHandle.Instance : NotNullHandle.Instance;
    /// <summary>
    /// The parser for <typeparamref name="T"/> over a result's columns, reused from cache when the same shape
    /// has been seen before and built otherwise. The cache is a linear scan kept to hold memory down, not for
    /// speed, so looking a parser up per query is slow, run commands through a cache that keeps the parser
    /// after first use instead.
    /// </summary>
    /// <param name="cols">The columns the result carries.</param>
    /// <param name="nullColHandler">
    /// An override of the root nullability, any <see cref="INullColHandler"/> implementation.
    /// When omitted or equal to <see cref="GetDefaultNullColHandler"/>, the type's own nullability applies
    /// </param>
    public static ITypeParser<T> GetTypeParser<T>(ref ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        nullColHandler ??= GetDefaultNullColHandler<T>();
        var readingInfos = ReadingInfos;
        foreach (var (schema, nullCol, p) in readingInfos) {
            if (p is ITypeParser<T> parser && nullCol == nullColHandler && cols.EquivalentTo(schema)) {
                cols = schema;
                return parser;
            }
        }
        lock (DefaultTypeParsingInfo.WriteLock) {
            var current = ReadingInfos;
            for (int i = readingInfos.Length; i < current.Length; i++) {
                var (schema, nullCol, p) = current[i];
                if (p is ITypeParser<T> parser && nullCol == nullColHandler && cols.EquivalentTo(schema)) {
                    cols = schema;
                    return parser;
                }
            }
            var unusual = MakeParser<T>(cols, nullColHandler);
            var updated = new (ColumnInfo[], INullColHandler, object)[current.Length + 1];
            current.CopyTo(updated, 0);
            updated[current.Length] = (cols, nullColHandler, unusual);
            ReadingInfos = updated;
            return unusual;
        }
    }
    private static ITypeParser<T> MakeParser<T>(ColumnInfo[] cols, INullColHandler nullColHandler) {
        ITypeParserMaker typeParserMaker = DefaultTypeParserMaker;
        foreach (var tpm in TypeParserMakers)
            if (tpm.CanHandle<T>()) {
                typeParserMaker = tpm;
                break;
            }
        if (!typeParserMaker.TryMakeParser<T>(nullColHandler, cols, out var info))
            throw new Exception($"cannot make the parser for {typeof(T)} with the schema ({string.Join(", ", cols.Select(c => $"{c.Type.ShortName()}{(c.IsNullable ? "?" : "")} {c.Name}"))})");
        return info;
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns of <typeparamref name="TSchema"/>, taken from its shape rather than a result.</summary>
    public static ITypeParser<T> GetTypeParser<TSchema, T>(out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        var res = GetTypeParser<T>(ref TypeSchema<TSchema>._schema, nullColHandler);
        cols = TypeSchema<TSchema>._schema;
        return res;
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from <paramref name="type"/>.</summary>
    public static ITypeParser<T> GetTypeParser<T>(Type type, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromType(type);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from a method's parameters.</summary>
    public static ITypeParser<T> GetTypeParser<T>(MethodBase method, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromMethod(method);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from a constructor's parameters.</summary>
    public static ITypeParser<T> GetTypeParser<T>(ConstructorInfo ctor, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromConstructor(ctor);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from a factory delegate's parameters.</summary>
    public static ITypeParser<T> GetTypeParser<T>(Delegate factory, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromMethod(factory.Method);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
}