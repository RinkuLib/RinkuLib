namespace Rinku.Mapping;
/// <summary>Controls column order and reuse while a type is mapped.</summary>
[Flags]
public enum UsageFlags {
    /// <summary>Requires the next read to follow the previous read in column order.</summary>
    SequentialRead = 0b001,
    /// <summary>Indicates that a column may be read without consuming it, even when it was already consumed.</summary>
    CanReuse = 0b010,
    /// <summary>Applies the setting to the complete nested value.</summary>
    Subtree = 0b100,
    /// <summary>Indicates that sequential reading should be removed.</summary>
    RemoveSequentialRead = int.MinValue
}
/// <summary>Holds the name and column usage settings for the current nested value.</summary>
public struct ColModifier(params INameComparer[] Comparers) {
    /// <summary>Gets or sets the column usage rules.</summary>
    public UsageFlags Flags = default;
    /// <summary>Gets the name rules from the root value to the current value.</summary>
    public readonly INameComparer[] Comparers = Comparers;
    /// <summary>
    /// Gets or sets the claim count at which <see cref="SwapFirstFlags"/> apply. A negative value disables it.
    /// </summary>
    public int SwapFirstAt = -1;
    /// <summary>Gets or sets the rules applied to the first claim of a nested value.</summary>
    public UsageFlags SwapFirstFlags = default;
    /// <summary>Creates settings with no name or column changes.</summary>
    public ColModifier() : this([]) { }
    /// <summary>
    /// Returns a copy with one name rule added for a nested value.
    /// </summary>
    /// <param name="comparer">The comparer to add to the chain.</param>
    /// <returns>A new modifier containing the updated chain.</returns>
    public readonly ColModifier Add(INameComparer comparer) {
        if (comparer is NoNameComparer)
            return this;
        if (Comparers.Length == 0)
            return new([comparer]) { Flags = Flags, SwapFirstAt = SwapFirstAt, SwapFirstFlags = SwapFirstFlags };
        int newLen = Comparers.Length + 1;
        var newArr = new INameComparer[newLen];
        Array.Copy(Comparers, newArr, Comparers.Length);
        newArr[newLen - 1] = comparer;
        return new ColModifier(newArr) { Flags = Flags, SwapFirstAt = SwapFirstAt, SwapFirstFlags = SwapFirstFlags };
    }
}
/// <summary>
/// Records the column usage and parent type at an earlier mapping point.
/// </summary>
public readonly struct RecursiveInfo(Type[] previousTypes, int colUsedToBeat) {
    /// <summary>Gets the latest parent type.</summary>
    public readonly Type LatestUsedType => PreviousTypes.Length > 0 ? PreviousTypes[^1] : typeof(RecursiveInfo);
    /// <summary>Gets the parent types already visited by this path.</summary>
    public readonly Type[] PreviousTypes = previousTypes;
    /// <summary>Gets the number of columns claimed when this path began.</summary>
    public readonly int ColUsedToBeat = colUsedToBeat;
    /// <summary>
    /// Tries to continue with another type. Returns <see langword="false"/> when the same path would repeat
    /// without claiming another column.
    /// </summary>
    public bool CanContinue(Type usedType, int currentColUsed, out RecursiveInfo currentUsage) {
        if (currentColUsed > ColUsedToBeat) {
            currentUsage = new([usedType], currentColUsed);
            return true;
        }
        for (int i = PreviousTypes.Length - 1; i >= 0; i--) {
            if (PreviousTypes[i] == usedType) {
                currentUsage = new([], currentColUsed);
                return false;
            }
        }
        currentUsage = new([..PreviousTypes, usedType], currentColUsed);
        return true;
    }
}
