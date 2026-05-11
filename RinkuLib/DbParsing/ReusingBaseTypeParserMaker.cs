using System.Data;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;
/// <summary></summary>
public delegate Type GetParserType(Type def, Type itemType, ref object?[] ctorArgs);
/// <summary></summary>
public class ReusingBaseTypeParserMaker(Type[] acceptedGenericDefinitions, GetParserType GetParserType, GetParserType? GetParserTypeWhenSimple = null) : ITypeParserMaker {
    private readonly Type[] acceptedGenericDefinitions = acceptedGenericDefinitions;
    private readonly GetParserType getParserType = GetParserType;
    private readonly GetParserType? getParserTypeWhenSimple = GetParserTypeWhenSimple;

    /// <inheritdoc/>
    public bool CanHandle<T>() 
        => typeof(T).IsGenericType && acceptedGenericDefinitions.Contains(typeof(T).GetGenericTypeDefinition());
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
            .GetMethod(nameof(TypeParser<>.GetTypeParser))
            ?.Invoke(null, [cols, nullColHandler]);

        if (itemParser is null)
            return false;
        var itemParserType = itemParser.GetType();

        Type collectionParserType;
        object?[] constructorArgs;

        if (getParserTypeWhenSimple is not null
            && itemParserType.IsGenericType
            && itemParserType.GetGenericTypeDefinition() == typeof(SimpleTypeParser<>)) {
            var behavior = (CommandBehavior)itemParserType.GetProperty(nameof(SimpleTypeParser<>.Behavior))!.GetValue(itemParser)!;
            var func = itemParserType.GetField(nameof(SimpleTypeParser<>.Parser))!.GetValue(itemParser)!;
            constructorArgs = [behavior, func];

            collectionParserType = getParserTypeWhenSimple(def, itemType, ref constructorArgs);

        }
        else {
            constructorArgs = [itemParser];
            collectionParserType = getParserType(def, itemType, ref constructorArgs);
        }

        parser = (ITypeParser<T>)Activator.CreateInstance(collectionParserType, constructorArgs)!;
        return parser != null;
    }
}