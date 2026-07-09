using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Commands;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary>
/// Manages the generation and caching of specialized parsers
/// </summary>
public static class TypeParser {
    /// <summary>
    /// The cache for parsers requested and root nullability.
    /// </summary>
    internal static readonly List<(ColumnInfo[] Schema, INullColHandler NullColHandler, object Parser)> ReadingInfos = [];
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
    /// <summary>The root nullability implied by <typeparamref name="T"/> itself</summary>
    public static INullColHandler GetDefaultNullColHandler<T>() => Nullable.GetUnderlyingType(typeof(T)) is not null
        ? NullableTypeHandle.Instance : NotNullHandle.Instance;
    /// <summary>
    /// Entry point for retrieving a parser. 
    /// It first searches the cache for a schema match; if none exists, it triggers 
    /// the generation of a new <see cref="DynamicMethod"/>.
    /// </summary>
    /// <param name="cols">The schema received from the data source.</param>
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
            var unusual = MakeParser<T>(cols, nullColHandler);
            ReadingInfos.Add(new(cols, nullColHandler, unusual));
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
    /// <inheritdoc/>
    public static ITypeParser<T> GetTypeParser<TSchema, T>(out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        var res = GetTypeParser<T>(ref TypeSchema<T>._schema, nullColHandler);
        cols = TypeSchema<T>._schema;
        return res;
    }
    /// <inheritdoc/>
    public static ITypeParser<T> GetTypeParser<T>(Type type, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromType(type);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <inheritdoc/>
    public static ITypeParser<T> GetTypeParser<T>(MethodBase method, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromMethod(method);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <inheritdoc/>
    public static ITypeParser<T> GetTypeParser<T>(ConstructorInfo ctor, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromConstructor(ctor);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
    /// <inheritdoc/>
    public static ITypeParser<T> GetTypeParser<T>(Delegate factory, out ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        cols = SchemaExtractor.FromMethod(factory.Method);
        return GetTypeParser<T>(ref cols, nullColHandler);
    }
}