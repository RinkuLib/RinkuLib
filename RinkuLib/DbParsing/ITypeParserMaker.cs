using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing; 
/// <summary>
/// An interface that intercept a type parsing info to use a different set of rule
/// </summary>
public interface ITypeParserMaker {
    /// <summary>
    /// Indicate if the maker can handle the given type
    /// </summary>
    public bool CanHandle<T>();
    /// <summary>
    /// The compilation core. Orchestrates the transition from metadata to IL.
    /// </summary>
    /// <remarks>
    /// <paramref name="nullColHandler"/> is the requested root nullability. It is
    /// <see cref="TypeParser{T}.DefaultNullColHandler"/> (the type's own nullability) unless a caller
    /// overrode it, and any <see cref="INullColHandler"/> implementation may arrive here.
    /// </remarks>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser);
}