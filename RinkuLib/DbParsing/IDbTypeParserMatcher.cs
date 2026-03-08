namespace RinkuLib.DbParsing;
/// <summary>
/// A marker interface. Types implementing this are automatically 
/// discovered and registered by <see cref="TypeParsingInfo"/>.
/// </summary>
public interface IDbReadable;
/// <summary>
/// A factory for creating <see cref="ParamInfo"/> instances.
/// </summary>
public interface IParamInfoMaker {
    /// <summary>
    /// Creates a matcher based on the provided reflection context.
    /// </summary>
    /// <param name="Type">The type of the member/parameter.</param>
    /// <param name="NullColHandler">The generated colHandler</param>
    /// <param name="NameComparer">The generated name comparer</param>
    /// <param name="name">The name of the member/parameter.</param>
    /// <param name="attributes">Metadata attributes attached to the member.</param>
    /// <param name="usageFlags">Provide the determined usage flags</param>
    /// <param name="param">Instance of the member</param>
    public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, string? name, object[] attributes, UsageFlags usageFlags, object? param);
}