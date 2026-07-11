using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing; 
/// <summary>
/// Builds the parser for the types it claims, the seam that adds a new result shape. Register one in
/// <see cref="TypeParser.TypeParserMakers"/>, ahead of the defaults, and the engine offers it each
/// <c>T</c> in turn.
/// </summary>
public interface ITypeParserMaker {
    /// <summary>Whether this maker claims <typeparamref name="T"/>.</summary>
    public bool CanHandle<T>();
    /// <summary>
    /// Builds the parser for <typeparamref name="T"/> over the given columns, or returns <see langword="false"/>
    /// to decline.
    /// </summary>
    /// <remarks>
    /// <paramref name="nullColHandler"/> is the requested root nullability. It is
    /// <see cref="TypeParser.GetDefaultNullColHandler{T}"/> (the type's own nullability) unless a caller
    /// overrode it, and any <see cref="INullColHandler"/> implementation may arrive here.
    /// </remarks>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser);
}