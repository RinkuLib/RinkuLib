using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Queries;
/// <summary>
/// One learned row parser, kept with the result shape it was learned for so a later run of the same shape
/// reuses it instead of inspecting the columns again.
/// </summary>
public struct ParsingCacheItem(ITypeParser Parser, int[] CondStates, ColumnInfo[] Schema, int ResultSetIndex) {
    /// <summary>
    /// The parser that reads a row into the target type.
    /// </summary>
    public ITypeParser Parser  = Parser;
    /// <summary>
    /// The conditional key states this parser is valid for, so it is only reused for a matching run.
    /// </summary>
    public int[] CondStates = CondStates;
    /// <summary>
    /// The columns the parser was built for.
    /// </summary>
    public ColumnInfo[] Schema = Schema;
    /// <summary>
    /// Which result set this parser belongs to, counting only sets that return columns.
    /// </summary>
    public int ResultSetIndex = ResultSetIndex;
}
/// <summary>Adds a learned parser to a cache, merging it with a matching entry when one is already there.</summary>
public static class ParsingCacheExtensions {
    /// <summary>
    /// Returns the cache with the parser for this result's columns folded in, reusing and widening a matching
    /// entry when one exists, otherwise adding a new one.
    /// </summary>
    public static ParsingCacheItem[] GetUpdatedCache<T>(this ParsingCacheItem[] parsingCache, IQueryText qt, bool[] usageMap, ColumnInfo[] schema, ITypeParser<T> cache, int resultSetIndex = 0) {
        for (var i = 0; i < parsingCache.Length; i++) {
            ref var item = ref parsingCache[i];
            if (item.ResultSetIndex == resultSetIndex && item.Parser is Func<DbDataReader, T> && schema.EquivalentTo(item.Schema)) {
                var currentLen = item.CondStates.Length;
                item.CondStates = GetUpdatedStates(usageMap, item.CondStates);
                if (item.CondStates.Length < currentLen) {
                    currentLen = item.CondStates.Length;
                    for (int j = i + 1; j < parsingCache.Length; j++)
                        if (parsingCache[j].CondStates.Length > currentLen)
                            (parsingCache[j], parsingCache[j - 1]) = (parsingCache[j - 1], parsingCache[j]);
                }
                return parsingCache;
            }
        }
        Span<int> condStates = stackalloc int[usageMap.Length];
        var count = 0;
        for (int i = 0; i < condStates.Length; i++)
            if (qt.IsInCondition(i))
                condStates[count++] = EncodeState(i, usageMap[i]);

        var newCache = new ParsingCacheItem[parsingCache.Length + 1];
        Array.Copy(parsingCache, 0, newCache, 1, parsingCache.Length);
        newCache[0] = new(cache, condStates[..count].ToArray(), schema, resultSetIndex);
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