namespace Rinku.Mapping;
/// <summary>
/// Marks a type as one Rinku can read, so it is picked up and registered automatically without a manual call.
/// </summary>
public interface IDbReadable;
/// <summary>
/// Provides the rules for reading one member or parameter.
/// Implement this interface to replace the mapping of selected members or parameters.
/// </summary>
public interface IParamInfoMaker {
    /// <summary>
    /// Creates the mapping settings for one member or construction parameter.
    /// </summary>
    /// <param name="Type">The member or parameter type.</param>
    /// <param name="NullColHandler">The current null rule.</param>
    /// <param name="NameComparer">The current name rule.</param>
    /// <param name="name">The member or parameter name.</param>
    /// <param name="attributes">The attributes declared on it.</param>
    /// <param name="usageFlags">The current column usage rules.</param>
    /// <param name="param">The reflected member or parameter when available.</param>
    public ParamInfo MakeMatcher(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, string? name, object[] attributes, UsageFlags usageFlags, object? param);
}
