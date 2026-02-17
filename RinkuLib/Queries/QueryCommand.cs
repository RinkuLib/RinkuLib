using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

[StructLayout(LayoutKind.Sequential)]
internal struct RawObject {
    public IntPtr MethodTable;
    public byte Data;
}
[StructLayout(LayoutKind.Sequential)]
internal class RawData { public byte Data; }
/// <summary>
/// The central orchestration engine that integrates SQL text generation (<see cref="Queries.QueryText"/>) 
/// with parameter metadata management (<see cref="QueryParameters"/>).
/// </summary>
/// <remarks>
/// <para><b>The Nervous System:</b></para>
/// <para>This class acts as the bridge between the raw user input array and the ADO.NET 
/// <see cref="IDbCommand"/>. It uses the <see cref="Tools.Mapper"/> as a shared coordinate system 
/// to partition a single array of variables into standard parameters, special handlers, 
/// and literal injections.</para>
/// </remarks>
public class QueryCommand : IQueryCommand, ICache {
    /// <inheritdoc/>
    public Mapper Mapper;
    Mapper IQueryCommand.Mapper => Mapper;
    int IQueryCommand.StartBaseHandlers => StartBaseHandlers;
    int IQueryCommand.StartSpecialHandlers => StartSpecialHandlers;
    int IQueryCommand.StartBoolCond => StartBoolCond;
    /// <summary> The registry for parameter metadata and caching strategies. </summary>
    public readonly QueryParameters Parameters;
    /// <summary> The SQL template and segment parsing logic. </summary>
    public readonly QueryText QueryText;
    /// <summary> The parsing items cached </summary>
    public ParsingCacheItem[] ParsingCache = [];
    private IntPtr[] _handles = [];
    private (MemberUsageDelegate Usage, MemberValueDelegate Value)[] _funcs = [];
    /// <summary>
    /// A lock shared to ensure thread safety across multiple <see cref="TypeAccessor"/> instances.
    /// </summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        TypeAccessorSharedLock = new();
    /// <summary>
    /// A lock shared to ensure thread safety across multiple parsingCache instances.
    /// </summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        ParsingCacheSharedLock = new();
    /// <inheritdoc/>
    public readonly int StartBaseHandlers;
    /// <inheritdoc/>
    public readonly int StartSpecialHandlers;
    /// <inheritdoc/>
    public readonly int StartBoolCond;
    /// <summary>Initialization of a query command from a SQL query template</summary>
    public QueryCommand(string query, char variableChar = default)
        : this(new QueryFactory(query, variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
    /// <summary>The direct call the the constructor</summary>
    protected QueryCommand(QueryFactory factory) {
        Mapper = factory.Mapper;
        var segments = factory.Segments;
        var queryString = factory.Query;
        StartBoolCond = Mapper.Count - factory.NbNonVarComment;
        StartBaseHandlers = StartBoolCond - factory.NbBaseHandlers;
        StartSpecialHandlers = StartBaseHandlers - factory.NbSpecialHandlers;
        var specialHandlers = GetHandlers(queryString, segments);
        QueryText = new(queryString, segments, factory.Conditions);
        Parameters = new(factory.NbNormalVar, specialHandlers);
    }
    /// <summary>
    /// Try getting the parsing cache without the schema
    /// </summary>
    public unsafe bool TryGetCache<T>(Span<bool> usageMap, out SchemaParser<T> cache) {
        bool* pUsage = (bool*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(usageMap));
        uint mapLen = (uint)usageMap.Length;
        var cacheArray = ParsingCache;
        int cacheLen = cacheArray.Length;

        for (int i = 0; i < cacheLen; i++) {
            ref var entry = ref cacheArray[i];
            var idxs = entry.FalseIndexes;
            int idxLen = idxs.Length;

            fixed (int* pIdx = &MemoryMarshal.GetArrayDataReference(idxs)) {
                if (idxLen == 1) {
                    if (*(pUsage + *pIdx))
                        goto NextEntry;
                }
                else {
                    for (int j = 0; j < idxLen; j++)
                        if (*(pUsage + pIdx[j]))
                            goto NextEntry;
                }
            }
            object parserObj = entry.Parser;
            if (parserObj is null) {
                cache = default;
                return false;
            }
            if (parserObj is Func<DbDataReader, T> p) {
                cache = new SchemaParser<T>(p, entry.CommandBehavior);
                return !NeedToCache(usageMap);
            }
        NextEntry: ;
        }
        cache = default;
        return false;
    }
    /// <summary>
    /// Try getting the parsing cache without the schema
    /// </summary>
    public unsafe bool TryGetCache<T>(object?[] usageMap, out SchemaParser<T> cache) {
        ref object? usageBase = ref MemoryMarshal.GetArrayDataReference(usageMap);

        var cacheArray = ParsingCache;
        int cacheLen = cacheArray.Length;

        for (int i = 0; i < cacheLen; i++) {
            ref var entry = ref cacheArray[i];
            int[] idxs = entry.FalseIndexes;
            int idxLen = idxs.Length;

            fixed (int* pIdx = &MemoryMarshal.GetArrayDataReference(idxs)) {
                if (idxLen == 1) {
                    if (Unsafe.Add(ref usageBase, *pIdx) is not null)
                        goto NextEntry;
                }
                else {
                    for (int j = 0; j < idxLen; j++) {
                        if (Unsafe.Add(ref usageBase, pIdx[j]) is not null)
                            goto NextEntry;
                    }
                }
            }

            object pObj = entry.Parser;
            if (pObj is null) {
                cache = default;
                return false;
            }

            if (pObj is Func<DbDataReader, T> p) {
                cache = new SchemaParser<T>(p, entry.CommandBehavior);
                return !NeedToCache(usageMap);
            }

        NextEntry: ;
        }

        cache = default;
        return false;
    }
    /// <summary>
    /// Update the parsing cache for a given schema
    /// </summary>
    public bool UpdateCache<T>(int[] falseIndexes, ColumnInfo[] schema, SchemaParser<T> cache) {
        lock (ParsingCacheSharedLock) {
            if (ParsingCache.Length == 0)
                ParsingCache = InitParsingCache();
            var cacheIndexesToRemove = new List<int>();
            var nbNullParser = 0;
            var ind = 0;
            int j;
            for (; ind < ParsingCache.Length; ind++) {
                ref var item = ref ParsingCache[ind];
                if (item.Parser is null) {
                    if (MatchIndexes(falseIndexes, item.FalseIndexes))
                        cacheIndexesToRemove.Add(ind);
                    nbNullParser++;
                }
                else if (item.Parser is Func<DbDataReader, T> && schema.Equal(item.Schema)) {
                    var currentLen = item.FalseIndexes.Length;
                    item.FalseIndexes = GetIntersection(item.FalseIndexes, falseIndexes);
                    if (item.FalseIndexes.Length < currentLen) {
                        currentLen = item.FalseIndexes.Length;
                        for (j = ind + 1; j < ParsingCache.Length; j++)
                            if (ParsingCache[j].FalseIndexes.Length > currentLen)
                                (ParsingCache[j], ParsingCache[j - 1]) = (ParsingCache[j - 1], ParsingCache[j]);
                    }
                    break;
                }
            }
            var allreadyIn = ind < ParsingCache.Length;
            if (cacheIndexesToRemove.Count == 0 && allreadyIn)
                return true;
            var newCache = new ParsingCacheItem[ParsingCache.Length - cacheIndexesToRemove.Count + (allreadyIn ? 0 : 1)];
            j = 0;
            int k = 0;
            for (int i = 0; i < nbNullParser; i++) {
                if (k < cacheIndexesToRemove.Count && cacheIndexesToRemove[k] == i) {
                    k++;
                    continue;
                }
                newCache[j++] = ParsingCache[i];
            }
            var falseIndexesLen = falseIndexes.Length;
            for (int i = nbNullParser; i < ParsingCache.Length; i++) {
                ref var item = ref ParsingCache[i];
                if (!allreadyIn && item.FalseIndexes.Length < falseIndexesLen)
                    newCache[j++] = new() { FalseIndexes = falseIndexes, CommandBehavior = cache.Behavior, Parser = cache.parser, Schema = schema };
                newCache[j++] = item;
            }
            if (!allreadyIn && newCache[^1].FalseIndexes is null)
                newCache[^1] = new() { FalseIndexes = falseIndexes, CommandBehavior = cache.Behavior, Parser = cache.parser, Schema = schema };
            ParsingCache = newCache;
            return true;
        }
    }
    private static int[] GetIntersection(int[] left, int[] right) {
        int leftLen = left.Length;
        int rightLen = right.Length;

        if (ReferenceEquals(left, right))
            return left;
        if (leftLen == 0)
            return left;
        if (rightLen == 0)
            return right;

        Span<int> intersectBuffer = stackalloc int[Math.Min(leftLen, rightLen)];
        int count = 0;

        for (int i = 0; i < leftLen; i++) {
            int target = left[i];
            for (int j = 0; j < rightLen; j++) {
                if (target == right[j]) {
                    intersectBuffer[count++] = target;
                    break;
                }
            }
        }
        if (count == leftLen)
            return left;
        if (count == rightLen)
            return right;
        return intersectBuffer[..count].ToArray();
    }
    private static bool MatchIndexes(int[] falseIndexes, int[] cachedFalseIndexes) {
        for (int i = 0; i < cachedFalseIndexes.Length; i++) {
            var look = cachedFalseIndexes[i];
            for (int j = 0; j < falseIndexes.Length; j++)
                if (j == look)
                    goto Match;
            return false;
        Match:;
        }
        return true;
    }

    private ParsingCacheItem[] InitParsingCache() {
        var conditions = QueryText.Conditions;
        HashSet<int[]> indexes = new(ArrayContentComparer<int>.Instance);
        var nbConds = conditions.Length - 1;
        for (int i = 0; i < nbConds; i++) {
            ref var cond = ref conditions[i];
            if (cond.NbConditionSkip >= 0) {
                indexes.Add([cond.CondIndex]);
                continue;
            }
            var count = conditions[i + 1].NbConditionSkip;
            var ii = new int[count + 1];
            ii[0] = cond.CondIndex;
            for (int j = 1; j <= count; j++)
                ii[j] = conditions[i + j].CondIndex;
        }
        var res = new ParsingCacheItem[indexes.Count];
        var ind = 0;
        foreach (var item in indexes) {
            res[ind++] = new() {
                FalseIndexes = item
            };
        }
        return res;
    }
    /// <summary>
    /// A fast way to identify if there are parameters that are used for the first time in the given state.
    /// </summary>
    /// <returns><see langword="false"/> no parameters are used for the first time</returns>
    public bool NeedToCache(Span<bool> usageMap)
        => Parameters.NeedToCache(usageMap);
    /// <summary>
    /// A fast way to identify if there are parameters that are used for the first time in the given state.
    /// </summary>
    /// <returns><see langword="false"/> no parameters are used for the first time</returns>
    public bool NeedToCache(object?[] variables)
        => Parameters.NeedToCache(variables);
    private SpecialHandler[] GetHandlers(string queryString, QuerySegment[] segments) {
        if (StartSpecialHandlers == StartBaseHandlers)
            return [];
        var handlers = new SpecialHandler[StartBaseHandlers - StartSpecialHandlers];
        for (int i = 0; i < segments.Length; i++) {
            ref var seg = ref segments[i];
            var h = seg.Handler;
            if (h is null || h != IQuerySegmentHandler.NotSet)
                continue;
            var last = seg.Start + seg.Length - 1;
            var ind = Mapper.GetIndex(queryString[seg.Start..(last - 1)]);
            ref var handler = ref handlers[ind - StartSpecialHandlers];
            if (handler is null) {
                var getter = SpecialHandler.SpecialHandlerGetter[queryString[last]];
                handler = getter(Mapper.GetKey(ind));
            }
            seg.Handler = handler;
            seg.ExcessOrInd = ind;
        }
        return handlers;
    }
    /// <summary>
    /// Synchronizes the command with a database provider's metadata. 
    /// Or any overrided comportement
    /// </summary>
    /// <remarks>
    /// Attempts to find a specialized <see cref="IDbParamInfoGetter"/> from 
    /// <see cref="IDbParamInfoGetter.ParamGetterMakers"/>. If no provider-specific 
    /// match is found, it falls back to the <see cref="DefaultParamCache"/>.
    /// </remarks>
    public void UpdateCache(IDbCommand cmd) {
        var makers = CollectionsMarshal.AsSpan(IDbParamInfoGetter.ParamGetterMakers);
        for (int i = 0; i < makers.Length; i++) {
            if (!makers[i](cmd, out var getter))
                continue;
            UpdateCache(getter);
            return;
        }
        UpdateCache(new DefaultParamCache(cmd));
    }
    private bool UpdateCache<T>(T infoGetter) where T : IDbParamInfoGetter {
        foreach (var item in infoGetter.EnumerateParameters()) {
            var ind = Mapper.GetIndex(item.Key);
            if (ind < 0 || ind >= StartBaseHandlers || Parameters.IsCached(ind))
                continue;
            Parameters.UpdateCache(ind, infoGetter.MakeInfoAt(item.Value));
        }
        Parameters.UpdateSpecialHandlers(infoGetter);
        Parameters.UpdateCachedIndexes();
        return true;
    }
    /// <summary>
    /// Provide a manual way to set a cache for a specific parameter
    /// </summary>
    public bool UpdateParamCache(string paramName, DbParamInfo paramInfo) {
        var ind = Mapper.GetIndex(paramName);
        if (ind < 0 || ind >= StartBaseHandlers)
            return false;
        Parameters.UpdateCache(ind, paramInfo);
        return true;

    }
    /// <summary>
    /// Synchronizes the database command with the current state of the entire query context.
    /// </summary>
    /// <param name="cmd">The command to be populated with parameters and SQL text.</param>
    /// <param name="variables">
    /// An array representing the full state of the query, including selects, conditions, 
    /// variables, and special handlers. This array must strictly follow the layout 
    /// defined by the <see cref="Mapper"/>.
    /// </param>
    /// <returns>True if the command was successfully prepared for execution.</returns>
    /// <remarks>
    /// This method consumes the <paramref name="variables"/> array as a unified state-snapshot. 
    /// While only the "Variable" and "Special Handler" sections of the array are used to 
    /// populate database parameters, the entire array (including "Select" and "Condition" states) 
    /// is passed to the <see cref="QueryText"/> parser to determine the final SQL structure.
    /// </remarks>
    public bool SetCommand(IDbCommand cmd, object?[] variables) {
        Debug.Assert(variables.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        ref string pKeys = ref Mapper.KeysStartPtr;

        for (int i = 0; i < varInfos.Length; i++) {
            var currentVar = Unsafe.Add(ref pVar, i);
            if (currentVar is not null)
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, currentVar);
        }

        ref object? pSpecialVar = ref Unsafe.Add(ref pVar, varInfos.Length);
        for (int i = 0; i < handlers.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is not null)
                handlers[i].Use(cmd, currentVar);
        }

        cmd.CommandText = QueryText.Parse(variables);

        return true;
    }
    /// <inheritdoc/>
    public bool SetCommand(DbCommand cmd, object?[] variables) {
        Debug.Assert(variables.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        ref string pKeys = ref Mapper.KeysStartPtr;

        for (int i = 0; i < varInfos.Length; i++) {
            var currentVar = Unsafe.Add(ref pVar, i);
            if (currentVar is not null)
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, currentVar);
        }

        ref object? pSpecialVar = ref Unsafe.Add(ref pVar, varInfos.Length);
        for (int i = 0; i < handlers.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is not null)
                handlers[i].Use(cmd, currentVar);
        }

        cmd.CommandText = QueryText.Parse(variables);

        return true;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand(IDbCommand cmd, object parameterObj, Span<bool> usageMap) {
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        if (type.IsValueType) {
            fixed (void* objPtr = &Unsafe.As<object, byte>(ref parameterObj)) {
                void* dataPtr = (*(byte**)objPtr) + IntPtr.Size;
                return SetCommand(cmd, GetAccessor(dataPtr, handle, type), usageMap);
            }
        }
        fixed (void* ptr = &Unsafe.As<object, byte>(ref parameterObj)) {
            void* instancePtr = *(void**)ptr;
            return SetCommand(cmd, GetAccessor(instancePtr, handle, type), usageMap);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand(DbCommand cmd, object parameterObj, Span<bool> usageMap) {
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        if (type.IsValueType) {
            fixed (void* objPtr = &Unsafe.As<object, byte>(ref parameterObj)) {
                void* dataPtr = (*(byte**)objPtr) + IntPtr.Size;
                return SetCommand(cmd, GetAccessor(dataPtr, handle, type), usageMap);
            }
        }
        fixed (void* ptr = &Unsafe.As<object, byte>(ref parameterObj)) {
            void* instancePtr = *(void**)ptr;
            return SetCommand(cmd, GetAccessor(instancePtr, handle, type), usageMap);
        }
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (typeof(T).IsValueType)
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            return SetCommand(cmd, GetAccessor(*(void**)ptr, handle, typeof(T)), usageMap);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (typeof(T).IsValueType)
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            return SetCommand(cmd, GetAccessor(*(void**)ptr, handle, typeof(T)), usageMap);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (typeof(T).IsValueType) {
            fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj))
                return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
        }
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            return SetCommand(cmd, GetAccessor(*(void**)ptr, handle, typeof(T)), usageMap);
        }
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (typeof(T).IsValueType) {
            fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj))
                return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
        }
        fixed (void* ptr = &Unsafe.As<T, byte>(ref parameterObj)) {
            return SetCommand(cmd, GetAccessor(*(void**)ptr, handle, typeof(T)), usageMap);
        }
    }
    /// <summary>
    /// Unsafe getter to get the cached accessor
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe TypeAccessor GetAccessor(void* ptr, IntPtr handle, Type type) {
        var hds = _handles;
        for (int i = 0; i < hds.Length; i++) {
            if (hds[i] != handle) {
                var (Usage, Value) = _funcs[i];
                return new TypeAccessor(ptr, Usage, Value);
            }
        }
        return AddAccessorCache(ptr, handle, type);
    }
    private unsafe TypeAccessor AddAccessorCache(void* ptr, nint handle, Type type) {
        lock (TypeAccessorSharedLock) {
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] == handle)
                    return new TypeAccessor(ptr, _funcs[i].Usage, _funcs[i].Value);

            var method = typeof(TypeAccessor<>).MakeGenericType(type).GetMethod(nameof(TypeAccessor<>.GetOrGenerate), BindingFlags.Public | BindingFlags.Static);
            var res = ((MemberUsageDelegate, MemberValueDelegate))method!.Invoke(null, [Mapper])!;
            int len = _handles.Length;
            var newH = new IntPtr[len + 1];
            var newF = new (MemberUsageDelegate, MemberValueDelegate)[len + 1];
            _handles.CopyTo(newH, 0);
            _funcs.CopyTo(newF, 0);
            newH[len] = handle;
            newF[len] = res;
            _handles = newH;
            _funcs = newF;
            return new TypeAccessor(ptr, res.Item1, res.Item2);
        }
    }

    private bool SetCommand(IDbCommand cmd, TypeAccessor accessor, Span<bool> usageMap) {
        Debug.Assert(usageMap.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref string pKeys = ref Mapper.KeysStartPtr;
        var total = Mapper.Count;
        int i = 0;
        for (; i < StartSpecialHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, accessor.GetValue(i));

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlers[i].Use(cmd, accessor.GetValue(i));

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        cmd.CommandText = QueryText.Parse(usageMap, accessor);
        return true;
    }
    private bool SetCommand(DbCommand cmd, TypeAccessor accessor, Span<bool> usageMap) {
        Debug.Assert(usageMap.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref string pKeys = ref Mapper.KeysStartPtr;
        var total = Mapper.Count;
        int i = 0;
        for (; i < StartSpecialHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, accessor.GetValue(i));

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlers[i].Use(cmd, accessor.GetValue(i));

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        cmd.CommandText = QueryText.Parse(usageMap, accessor);
        return true;
    }
}
