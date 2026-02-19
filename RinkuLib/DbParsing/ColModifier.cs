namespace RinkuLib.DbParsing;
/// <summary>
/// A hierarchical naming context that stores a chain of <see cref="INameComparer"/> instances.
/// Used to resolve nested column names (e.g., "ParentChildTarget") during recursive object mapping.
/// </summary>
public readonly struct ColModifier(params INameComparer[] Comparers) {
    /// <summary>Indicate if there is a modification on the name as a condition</summary>
    public bool DoesModify => _comparers.Length > 0;
    private readonly INameComparer[] _comparers = Comparers;
    /// <summary>Entry point without any modifications</summary>
    public ColModifier() : this([]) { }
    /// <summary>
    /// Creates a new <see cref="ColModifier"/> by appending a single <see cref="INameComparer"/> 
    /// to the current chain. Used when entering a nested object property.
    /// </summary>
    /// <param name="comparer">The comparer to add to the chain.</param>
    /// <returns>A new modifier containing the updated chain.</returns>
    public readonly ColModifier Add(INameComparer comparer) {
        if (comparer == NoNameComparer.Instance)
            return this;
        if (_comparers.Length == 0)
            return new([comparer]);
        int newLen = _comparers.Length + 1;
        var newArr = new INameComparer[newLen];
        Array.Copy(_comparers, newArr, _comparers.Length);
        newArr[newLen - 1] = comparer;
        return new ColModifier(newArr);
    }
    /// <summary>
    /// Checks if the provided <paramref name="colName"/> satisfies all prefixes in the chain.
    /// </summary>
    /// <param name="colName">The column name to evaluate.</param>
    /// <param name="remaining">The portion of the string remaining after all prefixes are stripped.</param>
    /// <returns><c>true</c> if all matchers in the chain are satisfied; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// For a chain ["Order", "User"], a column "OrderUserId" would set <paramref name="remaining"/> to "Id".
    /// </remarks>
    public readonly bool TryMatchStart(string colName, out ReadOnlySpan<char> remaining) {
        var matchers = _comparers;
        remaining = colName;
        if (matchers.Length == 0)
            return true;
        for (int i = 0; i < matchers.Length; i++)
            if (!matchers[i].TryMatchStart(remaining, out remaining))
                return false;
        return true;
    }
    /// <summary>
    /// Validates if the <paramref name="colName"/> matches the existing prefix chain 
    /// followed exactly by the provided <paramref name="NameComparer"/>.
    /// </summary>
    /// <param name="colName">The column name from the database.</param>
    /// <param name="NameComparer">The matcher for the final leaf property.</param>
    /// <returns><c>true</c> if the full path matches; otherwise, <c>false</c>.</returns>
    public readonly bool Match(string colName, INameComparer NameComparer) {
        var matchers = _comparers;
        ReadOnlySpan<char> remaining = colName;
        if (matchers.Length == 0)
            return NameComparer.Equals(remaining);
        for (int i = 0; i < matchers.Length; i++)
            if (!matchers[i].TryMatchStart(remaining, out remaining))
                return false;
        return NameComparer.Equals(remaining);
    }
}