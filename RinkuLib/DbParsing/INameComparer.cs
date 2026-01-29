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
public class NoNameComparer : INameComparer {
    public static readonly NoNameComparer Instance = new();
    public bool Equals(ReadOnlySpan<char> name) => true;
    public string GetDefaultName() => "";
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        remaining = colName;
        return true;
    }
    public INameComparer AddAltName(string altName)
        => new NameComparer(altName);
}
public class NameComparer(string Name) : INameComparer {
    public readonly string Name = Name;
    public bool Equals(ReadOnlySpan<char> name)
        => name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    public string GetDefaultName() => Name;
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        if (!colName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) {
            remaining = default;
            return false;
        }
        remaining = colName[Name.Length..];
        return true;
    }
    public INameComparer AddAltName(string altName) 
        => new NameComparerTwo(Name, altName);
}
public class NameComparerTwo(string Name, string AlternativeName) : INameComparer {
    public readonly string Name = Name;
    public readonly string AlternativeName = AlternativeName;
    public bool Equals(ReadOnlySpan<char> name) 
        => name.Equals(AlternativeName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    public string GetDefaultName() => Name;
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
    public INameComparer AddAltName(string altName)
        => new NameComparerArray([Name, AlternativeName, altName]);
}
public class NameComparerMany(string Name, string[] AlternativeNames) : INameComparer {
    public readonly string Name = Name;
    private string[] AlternativeNames = AlternativeNames;
    public bool Equals(ReadOnlySpan<char> name) {
        for (int i = 0; i < AlternativeNames.Length; i++)
            if (name.Equals(AlternativeNames[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return name.Equals(Name, StringComparison.OrdinalIgnoreCase);
    }
    public string GetDefaultName() => Name;
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
    public INameComparer AddAltName(string altName) {
        Interlocked.Exchange(ref AlternativeNames, [.. AlternativeNames, altName]);
        return this;
    }
}
public class NameComparerArray : INameComparer {
    private string[] Names;
    public NameComparerArray(string[] Names) {
        Debug.Assert(Names.Length > 0);
        this.Names = Names;
    }
    public bool Equals(ReadOnlySpan<char> name) {
        for (int i = 0; i < Names.Length; i++)
            if (name.Equals(Names[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    public string GetDefaultName() => Names[0];
    public bool TryMatchStart(ReadOnlySpan<char> colName, out ReadOnlySpan<char> remaining) {
        for (int i = 0; i < Names.Length; i++)
            if (colName.StartsWith(Names[i], StringComparison.OrdinalIgnoreCase)) {
                remaining = colName[Names[i].Length..];
                return true;
            }
        remaining = default;
        return false;
    }
    public INameComparer AddAltName(string altName) {
        Interlocked.Exchange(ref Names, [.. Names, altName]);
        return this;
    }
}