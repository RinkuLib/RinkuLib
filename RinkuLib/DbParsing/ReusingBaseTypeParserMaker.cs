using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;
/// <summary>Picks the parser type to build for a generic wrapper definition and its element type.</summary>
public delegate Type GetParserType(Type def, Type itemType, ref object?[] ctorArgs);
/// <summary>
/// A ready-made <see cref="ITypeParserMaker"/> for a shape that wraps a single element type, like
/// <c>List&lt;T&gt;</c> or a shape of your own. It builds the element parser and hands it to your wrapper's
/// constructor, so one registration maps the wrapper for any <c>T</c>. This is the easy road to a new result
/// shape, implement <see cref="ITypeParserMaker"/> directly only when it is not a generic wrapper.
/// </summary>
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
    private static object? GetItemParser(Type itemType, ref ColumnInfo[] cols, INullColHandler? nullColHandler) {
        object?[] args = [cols, nullColHandler];

        MethodInfo? method = null;
        foreach (var m in typeof(TypeParser).GetMethods()) {
            if (m.Name != nameof(TypeParser.GetTypeParser) || !m.IsGenericMethodDefinition)
                continue;

            var ps = m.GetParameters();
            if (ps.Length != 2 
                || ps[0].ParameterType != typeof(ColumnInfo[]).MakeByRefType() 
                || ps[1].ParameterType != typeof(INullColHandler))
                continue;

            method = m;
            break;
        }

        if (method is null)
            throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"{typeof(TypeParser).FullName}.{nameof(TypeParser.GetTypeParser)} was not found");

        var parser = method.MakeGenericMethod(itemType).Invoke(null, args);

        cols = (ColumnInfo[])args[0]!;
        return parser;
    }
    /// <inheritdoc/>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        parser = null;

        var type = typeof(T);
        if (!type.IsGenericType)
            return false;

        var def = type.GetGenericTypeDefinition();
        var itemType = type.GetGenericArguments()[0];

        INullColHandler? elementHandler;
        if (nullColHandler != TypeParser.GetDefaultNullColHandler<T>())
            elementHandler = nullColHandler;
        else
            elementHandler = elementNullability[Array.IndexOf(acceptedGenericDefinitions, def)];

        var itemParser = GetItemParser(itemType, ref cols, elementHandler);
        if (itemParser is null)
            return false;

        Type collectionParserType;
        object?[] constructorArgs;

        if (getParserTypeWhenSimple is not null && itemParser is ISimpleParser simple) {
            constructorArgs = [simple.Behavior, simple.RowParser];
            collectionParserType = getParserTypeWhenSimple(def, itemType, ref constructorArgs);
        }
        else {
            constructorArgs = [itemParser];
            collectionParserType = getParserType(def, itemType, ref constructorArgs);
        }

        parser = (ITypeParser<T>)Activator.CreateInstance(collectionParserType, constructorArgs)!;
        return true;
    }
}