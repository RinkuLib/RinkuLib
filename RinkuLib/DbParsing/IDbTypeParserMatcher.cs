using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// A marker interface. Types implementing this are automatically 
/// discovered and registered by <see cref="TypeParsingInfo"/>.
/// </summary>
public interface IDbReadable;
/// <summary>
/// Defines the matching and configuration logic for an individual item, 
/// such as a constructor parameter or a class member.
/// </summary>
public interface IDbTypeParserMatcher {
    /// <summary>
    /// The expected C# Type for this specific item.
    /// </summary>
    public Type TargetType { get; }
    /// <summary>
    /// The strategy used to handle null values from the schema for this specific item.
    /// </summary>
    public INullColHandler NullColHandler { get; set; }
    /// <summary>
    /// The strategy used to handle names from the schema for this specific item.
    /// </summary>
    public INameComparer NameComparer { get; set; }
    /// <summary>
    /// Registers an additional name that this item can match against in the schema.
    /// </summary>
    public void AddAltName(string altName);
    /// <summary>
    /// Returns the primary name (usually the property or parameter name) of the item.
    /// </summary>
    public string GetName();
    /// <summary>
    /// Configures the matching logic to trigger a logic jump/skip if the 
    /// corresponding schema value is null.
    /// </summary>
    public void SetInvalidOnNull(bool invalidOnNull);
    /// <summary>
    /// The core negotiation method for the item.
    /// </summary>
    /// <param name="declaringTypeArguments">Generic arguments from the parent type (if any).</param>
    /// <param name="columns">The available schema to match against.</param>
    /// <param name="colModifier">Transformation logic for column names/types.</param>
    /// <param name="colUsage">Identify which columns has allready been used.</param>
    /// <returns>A parser node if the item successfully matches a column; otherwise, null.</returns>
    public DbItemParser? TryGetParser(Type[] declaringTypeArguments, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage);
}
/// <summary>
/// Defines a custom strategy for matching a specific Type against a schema.
/// </summary>
public interface IDbTypeParserInfoMatcher {
    /// <summary>
    /// Negotiates the schema for the target type. 
    /// If successful, returns a parser node that handles the entire object construction.
    /// </summary>
    public DbItemParser? TryGetParser(Type closedTargetType, string paramName, INullColHandler nullColHandler, ColumnInfo[] columns, ColModifier colModifier, bool isNullable, ref ColumnUsage colUsage);
    /// <summary>Identify if the instance can actualy handle the <see cref="Type"/> of <paramref name="TargetType"/></summary>
    public bool CanUseType(Type TargetType);
}
/// <summary>
/// A factory for creating <see cref="IDbTypeParserMatcher"/> instances.
/// </summary>
public interface IDbReadingMatcherMaker {
    /// <summary>
    /// Creates a matcher based on the provided reflection context.
    /// </summary>
    /// <param name="type">The type of the member/parameter.</param>
    /// <param name="name">The name of the member/parameter.</param>
    /// <param name="attributes">Metadata attributes attached to the member.</param>
    /// <param name="param">Instance of the member</param>
    public IDbTypeParserMatcher MakeMatcher(Type type, string? name, object[] attributes, object? param);
}