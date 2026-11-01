using System.Data;
using System.Diagnostics.CodeAnalysis;
using Rinku.Internal;
using Rinku.Mapping.Parsers;

namespace Rinku.Mapping.Defaults;
/// <summary>Picks the parser type to build for a generic wrapper definition and its element type.</summary>
public delegate Type GetParserType(Type def, Type itemType, ref object?[] ctorArgs);
/// <summary>
/// A base class for a result type that wraps one element type.
/// Derive from this class for wrappers such as <c>Result&lt;T&gt;</c>.
/// Implement <see cref="ITypeParserMaker"/> directly for other result types.
/// </summary>
public class ReusingBaseTypeParserMaker(Type[] acceptedGenericDefinitions, GetParserType GetParserType, GetParserType? GetParserTypeWhenSimple = null, Func<Type, Type>? GetElementType = null) : ITypeParserMaker {
    private readonly Type[] acceptedGenericDefinitions = acceptedGenericDefinitions;
    private readonly INullColHandler?[] elementNullability = GetElementNullability(acceptedGenericDefinitions);
    private readonly GetParserType getParserType = GetParserType;
    private readonly GetParserType? getParserTypeWhenSimple = GetParserTypeWhenSimple;
    private readonly Func<Type, Type>? getElementType = GetElementType;
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
        var itemType = type.GetGenericArguments()[0];
        if (getElementType is not null)
            itemType = getElementType(itemType);

        INullColHandler? elementHandler;
        if (nullColHandler != TypeParser.GetDefaultNullColHandler<T>())
            elementHandler = nullColHandler;
        else
            elementHandler = elementNullability[Array.IndexOf(acceptedGenericDefinitions, def)];

        var itemParser = TypeParser.GetTypeParser(itemType, cols, elementHandler);

        Type collectionParserType;
        object?[] constructorArgs;

        if (getParserTypeWhenSimple is not null && itemParser is ISimpleParser simple) {
            constructorArgs = [simple.Behavior, simple.RowParser, itemParser];
            collectionParserType = getParserTypeWhenSimple(def, itemType, ref constructorArgs);
        }
        else {
            constructorArgs = [itemParser];
            collectionParserType = getParserType(def, itemType, ref constructorArgs);
        }

        parser = Activator.CreateInstance(collectionParserType, constructorArgs) as ITypeParser<T>
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"Could not create parser {collectionParserType}.");
        return true;
    }
}
