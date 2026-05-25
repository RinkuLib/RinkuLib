using System.Data;
using System.Reflection.Emit;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary></summary>
public static class TypeParser {
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
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
    /// <param name="nullColHandler">Specified nullability handling</param>
    public static ITypeParser<T> GetTypeParser(ref ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        lock (DefaultTypeParsingInfo.WriteLock) {
            for (int i = 0; i < ReadingInfos.Count; i++) {
                if (cols.EquivalentTo(ReadingInfos[i].Schema)) {
                    (cols, var Parser) = ReadingInfos[i];
                    return Parser;
                }
            }
            ITypeParserMaker typeParserMaker = TypeParser.DefaultTypeParserMaker;
            foreach (var tpm in TypeParser.TypeParserMakers)
                if (tpm.CanHandle<T>()) {
                    typeParserMaker = tpm;
                    break;
                }
            nullColHandler ??= Nullable.GetUnderlyingType(typeof(T)) is not null
                    ? NullableTypeHandle.Instance : NotNullHandle.Instance;

            if (!typeParserMaker.TryMakeParser<T>(nullColHandler, cols, out var info))
                throw new Exception($"cannot make the parser for {typeof(T)} with the schema ({string.Join(", ", cols.Select(c => $"{c.Type.ShortName()}{(c.IsNullable ? "?" : "")} {c.Name}"))})");
            ReadingInfos.Add(new(cols, info));
            return info;
        }
    }
}
internal static class Root;