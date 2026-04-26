using System.Data;
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
        var itemParserType = itemParser.GetType();
        bool isSimple = itemParserType.IsGenericType && itemParserType.GetGenericTypeDefinition() == typeof(SimpleTypeParser<>);

        Type collectionParserType;
        object[] constructorArgs;

        if (isSimple) {
            var behavior = (CommandBehavior)itemParserType.GetProperty(nameof(SimpleTypeParser<>.Behavior))!.GetValue(itemParser)!;
            var func = itemParserType.GetField(nameof(SimpleTypeParser<>.Parser))!.GetValue(itemParser)!;

            collectionParserType = (def == typeof(IEnumerable<>))
                ? typeof(FastEnumerableTypeParser<>).MakeGenericType(itemType)
                : typeof(FastListTypeParser<>).MakeGenericType(itemType);

            constructorArgs = [behavior, func];
        }
        else {
            collectionParserType = (def == typeof(IEnumerable<>))
                ? typeof(EnumerableTypeParser<>).MakeGenericType(itemType)
                : typeof(ListTypeParser<>).MakeGenericType(itemType);

            constructorArgs = [itemParser];
        }

        parser = (ITypeParser<T>)Activator.CreateInstance(collectionParserType, constructorArgs)!;
        return parser != null;
    }
}