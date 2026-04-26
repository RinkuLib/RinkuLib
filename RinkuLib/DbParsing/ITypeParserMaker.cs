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
    public bool TryMakeParser<T>(INullColHandler? nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser);
}