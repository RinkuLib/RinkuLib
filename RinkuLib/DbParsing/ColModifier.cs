namespace RinkuLib.DbParsing;
/// <summary>Inducate the column usage authorizations</summary>
[Flags]
public enum UsageFlags {
    /// <summary>Indicate that the next read should follow the precedent in columns order</summary>
    SequentialRead = 0b001,
    /// <summary>Indicate that an allready used column may be reused</summary>
    CanReuse = 0b010,
    /// <summary>Indicate the mode applies to a complex slot's whole subtree, not just its first column</summary>
    Subtree = 0b100,
    /// <summary>Indicate the sequential read should me removed</summary>
    RemoveSequentialRead = int.MinValue
}
/// <summary>
/// A hierarchical naming context that stores a chain of <see cref="INameComparer"/> instances.
/// Used to resolve nested column names (e.g., "ParentChildTarget") during recursive object mapping.
/// </summary>
public struct ColModifier(params INameComparer[] Comparers) {
    /// <summary>Informations on how the columns should be used</summary>
    public UsageFlags Flags = default;
    /// <summary>The current chain of comparers</summary>
    public readonly INameComparer[] Comparers = Comparers;
    /// <summary>
    /// The <see cref="ColumnUsage.NbUsed"/> captured when a slot-scope subtree was entered, or -1 when
    /// no such scope is active. The subtree's first consumed column (the read where <c>NbUsed</c> still
    /// equals this) applies <see cref="SwapFirstFlags"/>; every later read falls back to <see cref="Flags"/>.
    /// </summary>
    public int SwapFirstAt = -1;
    /// <summary>The flags applied to a slot-scope subtree's first consumed column.</summary>
    public UsageFlags SwapFirstFlags = default;
    /// <summary>Entry point without any modifications</summary>
    public ColModifier() : this([]) { }
    /// <summary>
    /// Creates a new <see cref="ColModifier"/> by appending a single <see cref="INameComparer"/> 
    /// to the current chain. Used when entering a nested object property.
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
/// Use to indicate a snapshot of the column usage and the type at a previous node point
/// </summary>
public readonly struct RecursiveInfo(Type[] previousTypes, int colUsedToBeat) {
    /// <summary>Indicate the latest type used (parent type)</summary>
    public readonly Type LatestUsedType => PreviousTypes.Length > 0 ? PreviousTypes[^1] : typeof(Root);
    /// <summary>The type that was used at that point</summary>
    public readonly Type[] PreviousTypes = previousTypes;
    /// <summary>The amount of column used at the start</summary>
    public readonly int ColUsedToBeat = colUsedToBeat;
    /// <summary>
    /// Use to generate the next usage in the chain
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