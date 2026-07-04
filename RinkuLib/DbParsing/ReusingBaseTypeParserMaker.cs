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
    private readonly INullColHandler?[] elementNullability = GetElementNullability(acceptedGenericDefinitions);
    private readonly GetParserType getParserType = GetParserType;
    private readonly GetParserType? getParserTypeWhenSimple = GetParserTypeWhenSimple;
    /// <summary>
    /// The element nullability a wrapper definition declares on its value parameter, resolved by
    /// <see cref="ParamInfo.GetDeclaredNullColHandler"/> (e.g. the <see cref="MaybeNullAttribute"/> on
    /// <see cref="OptionalNullable{T}"/>). <see langword="null"/> when nothing is declared, the element's
    /// own type decides.
    /// </summary>
    public static INullColHandler? DeclaredElementNullability(Type definition) {
        foreach (var ctor in definition.GetConstructors()) {
            var ps = ctor.GetParameters();
            if (ps.Length != 1)
                continue;
            var p = ps[0];
            var handler = ParamInfo.GetDeclaredNullColHandler(p.ParameterType, p.Name, p.GetCustomAttributes(true), p);
            if (handler is not null)
                return handler;
        }
        return null;
    }
    private static INullColHandler?[] GetElementNullability(Type[] defs) {
        var res = new INullColHandler?[defs.Length];
        for (int i = 0; i < defs.Length; i++)
            res[i] = DeclaredElementNullability(defs[i]);
        return res;
    }

    /// <inheritdoc/>
    public bool CanHandle<T>() 
        => typeof(T).IsGenericType && acceptedGenericDefinitions.Contains(typeof(T).GetGenericTypeDefinition());
    /// <inheritdoc/>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        parser = null;
        var type = typeof(T);

        if (!type.IsGenericType)
            return false;

        var def = type.GetGenericTypeDefinition();
        Type itemType = type.GetGenericArguments()[0];

        INullColHandler? elementHandler;
        if (nullColHandler != TypeParser<T>.DefaultNullColHandler)
            elementHandler = nullColHandler;
        else
            elementHandler = elementNullability[Array.IndexOf(acceptedGenericDefinitions, def)];

        var itemParser = typeof(TypeParser<>)
            .MakeGenericType(itemType)
            .GetMethod(nameof(TypeParser<>.GetTypeParser))
            ?.Invoke(null, [cols, elementHandler]);

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