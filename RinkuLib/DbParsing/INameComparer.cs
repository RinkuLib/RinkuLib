using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RinkuLib.DbParsing;
/// <summary>
/// Defines the contract for comparing database column names against member identifiers.
/// </summary>
public interface INameComparer {
    private const string TargetAttributeName = "TrueNameAttribute";

    /// <summary>
    /// Attempts to extract an override name from a provided attribute instance.
    /// This is adaptive: it uses the first public string property found on the attribute.
    /// </summary>
    static bool TryGetTrueName(object attribute, [MaybeNullWhen(false)] out string trueName) {
        trueName = null;

        if (attribute.GetType().Name != TargetAttributeName)
            return false;

        foreach (var prop in attribute.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            trueName = prop.GetValue(attribute) as string;
            if (trueName is not null)
                return true;
        }

        return false;
    }
    /// <summary>Returns the primary or first registered name used for identification.</summary>
    public string GetDefaultName();

    /// <summary>
    /// Validates if the <paramref name="colName"/> matches this comparer's logic.
    /// If successful, it consumes its portion of the name and continues the chain.
    /// </summary>
    /// <param name="colName">The remaining portion of the column name to match.</param>
    /// <param name="nameComparers">The preceding chain of comparers to validate against.</param>
    /// <returns><c>true</c> if the full path matches; otherwise, <c>false</c>.</returns>
    bool Match(ReadOnlySpan<char> colName, Span<INameComparer> nameComparers);
    /// <summary>Checks if this comparer or its children contains the specified name.</summary>
    public bool Contains(string name);
}
/// <summary>Defines the ability to incorporate a simple alternative name with span 1.</summary>
public interface INameComparerThatCanAdd : INameComparer {
    /// <summary>Attempts to add a standard alternative. Returns a new instance if successful; otherwise <c>null</c>.</summary>
    public INameComparer? TryAdd(string name);
}

/// <summary>Defines the ability to absorb another entire <see cref="INameComparer"/> into the current structure.</summary>
public interface INameComparerThatCanAddAComparer : INameComparer {
    /// <summary>Attempts to merge another comparer. Returns the consolidated result if successful; otherwise <c>null</c>.</summary>
    public INameComparer? TryAdd(INameComparer other);
}
/// <summary>Defines the ability to remove a specific string identifier from the matching logic.</summary>
public interface INameComparerThatCanRemove : INameComparer {
    /// <summary>Attempts to remove a name. Returns the updated comparer if the name was removed; otherwise <c>null</c>.</summary>
    public INameComparer? TryRemove(string name);
}

/// <summary>Defines the ability to remove a specific comparer instance from a group or chain.</summary>
public interface INameComparerThatCanRemoveAComparer : INameComparer {
    /// <summary>Attempts to remove the target comparer. Returns the updated structure if found; otherwise <c>null</c>.</summary>
    public INameComparer? TryRemove(INameComparer other);
}
/// <summary>
/// Combines all mutative capabilities. Standard implementations will implement this 
/// to ensure they can be fully managed by the orchestration logic.
/// </summary>
public interface IMutatableNameComparer :
    INameComparerThatCanAdd,
    INameComparerThatCanAddAComparer,
    INameComparerThatCanRemove,
    INameComparerThatCanRemoveAComparer { }
/// <summary></summary>
public static class NameComparerHelper {
    /// <summary>Helper that passes the chain of matches down</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MatchNext(this Span<INameComparer> nameComparers, ReadOnlySpan<char> remaining, int count = 1)
        => nameComparers.Length < count ? remaining.Length == 0 : nameComparers[^count].Match(remaining, nameComparers[..^count]);
    /// <summary>
    /// Adds a new alternative name to the existing comparer structure.
    /// </summary>
    public static INameComparer AddAltName(this INameComparer current, string altName) {
        if (current.Contains(altName))
            return current;
        if (current is NoNameComparer)
            return new NameComparer(altName);

        if (current is INameComparerThatCanAdd growable) {
            var result = growable.TryAdd(altName);
            if (result != null)
                return result;
        }

        return new JoinedNameComparer(current, new NameComparer(altName));
    }

    /// <summary>
    /// Adds a new alternative comparer to the existing comparer structure.
    /// </summary>
    public static INameComparer AddComparer(this INameComparer current, INameComparer other) {
        if (other is NoNameComparer || ReferenceEquals(current, other))
            return current;
        if (current is NoNameComparer)
            return other;

        if (current is INameComparerThatCanAddAComparer growable) {
            var result = growable.TryAdd(other);
            if (result != null)
                return result;
        }

        return new JoinedNameComparer(current, other);
    }

    /// <summary>
    /// Safely removes a name from the tree if the comparer supports it.
    /// </summary>
    public static INameComparer RemoveName(this INameComparer current, string name) {
        if (current is INameComparerThatCanRemove removable)
            return removable.TryRemove(name) ?? current;
        return current;
    }

    /// <summary>
    /// Safely removes a specific comparer from the tree.
    /// </summary>
    public static INameComparer RemoveComparer(this INameComparer current, INameComparer target) {
        if (current is INameComparerThatCanRemoveAComparer removable)
            return removable.TryRemove(target) ?? current;
        return ReferenceEquals(current, target) ? NoNameComparer.Instance : current;
    }
}