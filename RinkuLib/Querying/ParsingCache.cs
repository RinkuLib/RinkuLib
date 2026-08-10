using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinku.Mapping.Parsers;

namespace Rinku.Querying;
/// <summary>
/// One learned row parser, kept with the command conditions and result-set position that select it on a later
/// run. Schema compatibility belongs to the parser and is resolved before this optimized command cache.
/// </summary>
public struct ParsingCacheItem(ITypeParser Parser, int[] CondStates, int ResultSetIndex) {
    /// <summary>
    /// The parser that reads a row into the target type.
    /// </summary>
    public ITypeParser Parser  = Parser;
    /// <summary>
    /// The conditional key states this parser is valid for, so it is only reused for a matching run.
    /// </summary>
    public int[] CondStates = CondStates;
    /// <summary>
    /// Which result set this parser belongs to, counting only sets that return columns.
    /// </summary>
    public int ResultSetIndex = ResultSetIndex;
}
/// <summary>Adds a learned parser to a cache, merging it with a matching entry when one is already there.</summary>
public static class ParsingCacheExtensions {
    /// <summary>
    /// Returns the cache with the parser folded in, reusing and widening the entry for the same parser instance
    /// when one exists, otherwise adding a new one.
    /// </summary>
    /// <remarks>
    /// The array it is given is never written into. Widening an entry copies first, so a lookup running
    /// beside this one reads a whole array or the one before it, never an entry half moved.
    /// </remarks>
    public static ParsingCacheItem[] GetUpdatedCache<T>(this ParsingCacheItem[] parsingCache, IQueryText qt, bool[] usageMap, ITypeParser<T> cache, int resultSetIndex = 0) {
        for (var i = 0; i < parsingCache.Length; i++) {
            ref var item = ref parsingCache[i];
            if (item.ResultSetIndex == resultSetIndex && ReferenceEquals(item.Parser, cache)) {
                var currentLen = item.CondStates.Length;
                var widened = GetUpdatedStates(usageMap, item.CondStates);
                if (widened.Length == currentLen)
                    return parsingCache;
                var merged = (ParsingCacheItem[])parsingCache.Clone();
                merged[i].CondStates = widened;
                currentLen = widened.Length;
                for (int j = i + 1; j < merged.Length; j++)
                    if (merged[j].CondStates.Length > currentLen)
                        (merged[j], merged[j - 1]) = (merged[j - 1], merged[j]);
                return merged;
            }
        }
        Span<int> condStates = stackalloc int[usageMap.Length];
        var count = 0;
        for (int i = 0; i < condStates.Length; i++)
            if (qt.IsInCondition(i))
                condStates[count++] = EncodeState(i, usageMap[i]);

        var newCache = new ParsingCacheItem[parsingCache.Length + 1];
        Array.Copy(parsingCache, 0, newCache, 1, parsingCache.Length);
        newCache[0] = new(cache, condStates[..count].ToArray(), resultSetIndex);
        return newCache;
    }
    private static int EncodeState(int index, bool state) => (index << 1) | (state ? 1 : 0);
    private static int[] GetUpdatedStates(bool[] usageMap, int[] condState) {
        int idxLen = condState.Length;
        Span<int> intersectBuffer = stackalloc int[idxLen];
        int count = 0;

        ref int pBase = ref MemoryMarshal.GetArrayDataReference(condState);
        for (int j = 0; j < idxLen; j++) {
            int packed = Unsafe.Add(ref pBase, j);
            if (usageMap[packed >> 1] == ((packed & 1) != 0))
                intersectBuffer[count++] = packed;
        }
        if (count == idxLen)
            return condState;
        return intersectBuffer[..count].ToArray();
    }
}
