using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Represent an cached item used for parsing a <see cref="DbDataReader"/>
/// </summary>
public struct ParsingCacheItem(object Parser, int[] CondStates, ColumnInfo[] Schema, CommandBehavior CommandBehavior, int ResultSetIndex) {
    /// <summary>
    /// The actual parser func
    /// </summary>
    public object Parser  = Parser;
    /// <summary>
    /// The indexes at which the condition muts be false
    /// </summary>
    public int[] CondStates = CondStates;
    /// <summary>
    /// The schema for which the <see cref="Parser"/> is for
    /// </summary>
    public ColumnInfo[] Schema = Schema;
    /// <summary>
    /// The default behavior of the reader
    /// </summary>
    public CommandBehavior CommandBehavior = CommandBehavior;
    /// <summary>
    /// The index of the corresponding result set, (non returning set (FieldCount == 0) are not taken into consideration)
    /// </summary>
    public int ResultSetIndex = ResultSetIndex;
}
/// <summary></summary>
public static class ParsingCacheExtensions {
    /// <summary>
    /// Update the parsing cache for a given schema
    /// </summary>
    public static ParsingCacheItem[] GetUpdatedCache<T>(this ParsingCacheItem[] parsingCache, IQueryText qt, bool[] usageMap, ColumnInfo[] schema, SchemaParser<T> cache, int resultSetIndex = 0) {
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
        newCache[0] = new(cache.parser, condStates[..count].ToArray(), schema, cache.Behavior, resultSetIndex);
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