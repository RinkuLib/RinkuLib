namespace RinkuLib.DbParsing;
/// <summary>Inducate the column usage authorizations</summary>
[Flags]
public enum UsageFlags {
    /// <summary>Indicate that the next read should follow the precedent in columns order</summary>
    SequentialRead = 0b01,
    /// <summary>Indicate that an allready used column may be reused</summary>
    CanReuse = 0b10,
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
            return new([comparer]);
        int newLen = Comparers.Length + 1;
        var newArr = new INameComparer[newLen];
        Array.Copy(Comparers, newArr, Comparers.Length);
        newArr[newLen - 1] = comparer;
        return new ColModifier(newArr);
    }
}