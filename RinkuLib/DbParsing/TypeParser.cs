using System.Data;
using System.Reflection.Emit;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary></summary>
public static class TypeParser {
    /// <summary>
    /// The cache for parsers requested with an unusual root nullability, anything other than the type's
    /// own <see cref="TypeParser{T}.DefaultNullColHandler"/>. Shared across all types: unusual requests
    /// are rare, so they are concentrated here instead of growing a second long-lived cache per type.
    /// </summary>
    internal static readonly List<(Type Type, ColumnInfo[] Schema, INullColHandler NullColHandler, object Parser)> UnusualReadingInfos = [];
    /// <summary></summary>
    public static readonly DefaultTypeParserMaker DefaultTypeParserMaker = new();
    /// <summary></summary>
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
}
/// <summary>
/// Manages the generation and caching of specialized parsers for <typeparamref name="T"/>.
/// </summary>
public static class TypeParser<T> {
    private static readonly List<(ColumnInfo[] Schema, ITypeParser<T> Parser)> ReadingInfos = [];
    /// <summary>The root nullability implied by <typeparamref name="T"/> itself</summary>
    public static readonly INullColHandler DefaultNullColHandler = Nullable.GetUnderlyingType(typeof(T)) is not null
        ? NullableTypeHandle.Instance : NotNullHandle.Instance;
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="nullColHandler">
    /// An override of the root nullability, any <see cref="INullColHandler"/> implementation.
    /// When omitted or equal to <see cref="DefaultNullColHandler"/>, the type's own nullability applies
    /// and this type's fast schema-keyed cache is used. Unusual handlers land in one slower cache shared
    /// across all types, keyed by type, schema, and handler instance, so reuse the same instance across
    /// calls to hit it.
    /// </param>
    public static ITypeParser<T> GetTypeParser(ref ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        lock (DefaultTypeParsingInfo.WriteLock) {
            if (nullColHandler is not null && nullColHandler != DefaultNullColHandler) {
                var shared = TypeParser.UnusualReadingInfos;
                for (int i = 0; i < shared.Count; i++) {
                    var entry = shared[i];
                    if (entry.Type == typeof(T) && entry.NullColHandler == nullColHandler && cols.EquivalentTo(entry.Schema)) {
                        cols = entry.Schema;
                        return (ITypeParser<T>)entry.Parser;
                    }
                }
                var unusual = MakeParser(cols, nullColHandler);
                shared.Add(new(typeof(T), cols, nullColHandler, unusual));
                return unusual;
            }
            for (int i = 0; i < ReadingInfos.Count; i++) {
                if (cols.EquivalentTo(ReadingInfos[i].Schema)) {
                    (cols, var Parser) = ReadingInfos[i];
                    return Parser;
                }
            }
            var info = MakeParser(cols, DefaultNullColHandler);
            ReadingInfos.Add(new(cols, info));
            return info;
        }
    }
    private static ITypeParser<T> MakeParser(ColumnInfo[] cols, INullColHandler nullColHandler) {
        ITypeParserMaker typeParserMaker = TypeParser.DefaultTypeParserMaker;
        foreach (var tpm in TypeParser.TypeParserMakers)
            if (tpm.CanHandle<T>()) {
                typeParserMaker = tpm;
                break;
            }
        if (!typeParserMaker.TryMakeParser<T>(nullColHandler, cols, out var info))
            throw new Exception($"cannot make the parser for {typeof(T)} with the schema ({string.Join(", ", cols.Select(c => $"{c.Type.ShortName()}{(c.IsNullable ? "?" : "")} {c.Name}"))})");
        return info;
    }
}
internal static class Root;