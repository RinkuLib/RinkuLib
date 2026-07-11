namespace RinkuLib.DbParsing;
/// <summary>
/// Marks a type as one Rinku can read, so it is picked up and registered automatically without a manual call.
/// </summary>
public interface IDbReadable;
/// <summary>
/// Builds the plan for reading one member or parameter, how it is named, whether it may be null, and how its
/// value is produced. The seam for taking over a member's mapping.
/// </summary>
public interface IParamInfoMaker {
    /// <summary>
    /// Builds the read plan for a member or parameter from its reflection metadata.
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