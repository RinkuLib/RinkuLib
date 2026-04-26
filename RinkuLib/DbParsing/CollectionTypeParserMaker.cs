using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary></summary>
public class CollectionTypeParserMaker : ITypeParserMaker {
    /// <inheritdoc/>
    public bool CanHandle<T>() {
        if (!typeof(T).IsGenericType) return false;
        var def = typeof(T).GetGenericTypeDefinition();
        return def == typeof(List<>) || def == typeof(IEnumerable<>);
    }
    private static readonly Type[] MethodTypes = [typeof(ColumnInfo[]).MakeByRefType(), typeof(INullColHandler)];
    /// <inheritdoc/>
    public bool TryMakeParser<T>(INullColHandler? nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        parser = null;
        var type = typeof(T);

        if (!type.IsGenericType)
            return false;

        var def = type.GetGenericTypeDefinition();
        Type itemType = type.GetGenericArguments()[0];

        var itemParser = typeof(TypeParser<>)
            .MakeGenericType(itemType)
            .GetMethod(nameof(TypeParser<>.GetTypeParser), BindingFlags.Public | BindingFlags.Static, null, MethodTypes, null)
            ?.Invoke(null, [cols, nullColHandler]);

        if (itemParser is null)
            return false;

        Type collectionParserType = def == typeof(IEnumerable<>)
            ? typeof(EnumerableTypeParser<>).MakeGenericType(itemType)
            : typeof(ListTypeParser<>).MakeGenericType(itemType);

        parser = (ITypeParser<T>)Activator.CreateInstance(collectionParserType, itemParser)!;

        return parser != null;
    }
}