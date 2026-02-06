using System.Diagnostics;

namespace RinkuLib.DbParsing;
/// <summary>
/// Defines an alternative name for a parameter, property, or field during database column matching.
/// </summary>

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class AltAttribute(string AlternativeName) : Attribute {
    /// <summary>The additional name to check during negotiation.</summary>
    public readonly string AlternativeName = AlternativeName;
}
/// <summary>
/// Defines the contract for comparing database column names against member identifiers.
/// </summary>
public interface INameComparer {
    /// <summary>Returns the primary or first registered name.</summary>
    public string GetDefaultName();
    /// <summary>
    /// Checks if a column name starts with any of the registered names. 
    /// Used for nested prefix matching (e.g., "UserId" matches "User" and remains "Id").
    /// </summary>
    /// <param name="colName">The column name from the database.</param>
    /// <param name="remaining">The slice of the string remaining after the match.</param>
    /// <returns><c>true</c> if a match is found; otherwise, <c>false</c>.</returns>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining);
    /// <summary>Checks for an exact, case-insensitive match.</summary>
    public bool Equals(ReadOnlySpan<char> name);
    /// <summary>returns a comparer that includes the new alternative name.</summary>
    public INameComparer AddAltName(string altName);
}
/// <summary>Match by default</summary>
public class NoNameComparer : INameComparer {
    /// <summary>Singleton</summary>
    public static readonly NoNameComparer Instance = new();
    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> name) => true;
    /// <inheritdoc/>
    public string GetDefaultName() => "";
    /// <inheritdoc/>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        remaining = colName;
        return true;
    }
    /// <inheritdoc/>
    public INameComparer AddAltName(string altName)
        => new NameComparer(altName);
}
/// <summary>Match using one name</summary>
public class NameComparer(string Name) : INameComparer {
    /// <summary>The name to match to</summary>
    public readonly string Name = Name;
    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> name)
        => name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        if (!colName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) {
            remaining = default;
            return false;
        }
        remaining = colName[Name.Length..];
        return true;
    }
    /// <inheritdoc/>
    public INameComparer AddAltName(string altName) 
        => new NameComparerTwo(Name, altName);
}
/// <summary>Match using two names</summary>
public class NameComparerTwo(string Name, string AlternativeName) : INameComparer {
    /// <summary>The name to match to</summary>
    public readonly string Name = Name;
    /// <summary>The alternative name to match to</summary>
    public readonly string AlternativeName = AlternativeName;
    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> name) 
        => name.Equals(AlternativeName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        if (colName.StartsWith(AlternativeName, StringComparison.OrdinalIgnoreCase)) {
            remaining = colName[AlternativeName.Length..];
            return true;
        }
        if (colName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) {
            remaining = colName[Name.Length..];
            return true;
        }
        remaining = default;
        return false;
    }
    /// <inheritdoc/>
    public INameComparer AddAltName(string altName)
        => new NameComparerArray([Name, AlternativeName, altName]);
}
/// <summary>Match using many names</summary>
public class NameComparerMany(string Name, string[] AlternativeNames) : INameComparer {
    /// <summary>The name to match to</summary>
    public readonly string Name = Name;
    /// <summary>The alternative names to match to</summary>
    private string[] AlternativeNames = AlternativeNames;
    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> name) {
        for (int i = 0; i < AlternativeNames.Length; i++)
            if (name.Equals(AlternativeNames[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    }
    /// <inheritdoc/>
    public string GetDefaultName() => Name;
    /// <inheritdoc/>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        for (int i = 0; i < AlternativeNames.Length; i++)
            if (colName.StartsWith(AlternativeNames[i], StringComparison.OrdinalIgnoreCase)) {
                remaining = colName[AlternativeNames[i].Length..];
                return true;
            }
        if (colName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) {
            remaining = colName[Name.Length..];
            return true;
        }
        remaining = default;
        return false;
    }
    /// <inheritdoc/>
    public INameComparer AddAltName(string altName) {
        Interlocked.Exchange(ref AlternativeNames, [.. AlternativeNames, altName]);
        return this;
    }
}
/// <summary>Match using many names</summary>
public class NameComparerArray : INameComparer {
    /// <summary>The alternative name to match to</summary>
    private string[] Names;
    /// <summary></summary>
    public NameComparerArray(string[] Names) {
        Debug.Assert(Names.Length > 0);
        this.Names = Names;
    }
    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> name) {
        for (int i = 0; i < Names.Length; i++)
            if (name.Equals(Names[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    /// <inheritdoc/>
    public string GetDefaultName() => Names[0];
    /// <inheritdoc/>
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        for (int i = 0; i < Names.Length; i++)
            if (colName.StartsWith(Names[i], StringComparison.OrdinalIgnoreCase)) {
                remaining = colName[Names[i].Length..];
                return true;
            }
        remaining = default;
        return false;
    }
    /// <inheritdoc/>
    public INameComparer AddAltName(string altName) {
        Interlocked.Exchange(ref Names, [.. Names, altName]);
        return this;
    }
}