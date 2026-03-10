using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Queries;
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
    public readonly Mapper Mapper;
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
    private TypeAccessorCache[] _funcs = [];
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
        var specialHandlers = SpecialHandler.GetHandlers(StartSpecialHandlers, StartBaseHandlers, Mapper, queryString, segments);
        QueryText = new(queryString, segments, factory.Conditions);
        Parameters = new(factory.NbNormalVar, specialHandlers);
    }
    /// <summary>
    /// Try getting the parsing cache without the schema
    /// </summary>
    public bool TryGetCache<T>(Span<bool> usageMap, out SchemaParser<T> cache, int resultSetIndex = 0) {
        ref bool pUsage = ref MemoryMarshal.GetReference(usageMap);
        uint mapLen = (uint)usageMap.Length;
        var cacheArray = ParsingCache;
        int cacheLen = cacheArray.Length;

        for (int i = 0; i < cacheLen; i++) {
            ref var entry = ref cacheArray[i];
            int idxLen = entry.CondStates.Length;
            ref int pBase = ref MemoryMarshal.GetReference(entry.CondStates);
            for (int j = 0; j < idxLen; j++) {
                int packed = Unsafe.Add(ref pBase, j);
                if (Unsafe.Add(ref pUsage, packed >> 1) != ((packed & 1) != 0))
                    goto NextEntry;
            }
            object parserObj = entry.Parser;
            if (parserObj is null || entry.ResultSetIndex != resultSetIndex) {
                cache = default;
                return false;
            }
            if (parserObj is Func<DbDataReader, T> p) {
                cache = new SchemaParser<T>(p, entry.CommandBehavior);
                return !NeedToCache(usageMap);
            }
        NextEntry:
            ;
        }
        cache = default;
        return false;
    }
    /// <summary>
    /// Try getting the parsing cache without the schema
    /// </summary>
    public bool TryGetCache<T>(object?[] usageMap, out SchemaParser<T> cache, int resultSetIndex = 0) {
        ref object? usageBase = ref MemoryMarshal.GetArrayDataReference(usageMap);

        var cacheArray = ParsingCache;
        int cacheLen = cacheArray.Length;

        for (int i = 0; i < cacheLen; i++) {
            ref var entry = ref cacheArray[i];
            int idxLen = entry.CondStates.Length;
            ref int pBase = ref MemoryMarshal.GetArrayDataReference(entry.CondStates);
            for (int j = 0; j < idxLen; j++) {
                int packed = Unsafe.Add(ref pBase, j);
                if ((Unsafe.Add(ref usageBase, packed >> 1) is not null) != ((packed & 1) != 0))
                    goto NextEntry;
            }

            object pObj = entry.Parser;
            if (pObj is null || entry.ResultSetIndex != resultSetIndex) {
                cache = default;
                return false;
            }

            if (pObj is Func<DbDataReader, T> p) {
                cache = new SchemaParser<T>(p, entry.CommandBehavior);
                return !NeedToCache(usageMap);
            }

        NextEntry:
            ;
        }

        cache = default;
        return false;
    }
    /// <summary>
    /// Update the parsing cache for a given schema
    /// </summary>
    public void UpdateParseCache<T>(bool[] usageMap, ColumnInfo[] schema, SchemaParser<T> cache, int resultSetIndex = 0) {
        lock (ParsingCacheSharedLock) { 
            ParsingCache = ParsingCache.GetUpdatedCache(QueryText, usageMap, schema, cache, resultSetIndex);
        }
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
            if (currentVar is null)
                continue;
            if (currentVar is IEnumerable && currentVar is not string && !HasAny(ref Unsafe.As<object, IEnumerable>(ref currentVar))) {
                currentVar = null;
                continue;
            }
            handlers[i].Use(cmd, currentVar);
        }

        cmd.CommandText = QueryText.Parse(variables);

        return true;
    }
    internal static bool HasAny(ref IEnumerable value) {
        if (value is not IEnumerable source)
            return true;
        if (source is IEnumerable<object> enu && enu.TryGetNonEnumeratedCount(out var nb)) {
            if (nb <= 0)
                return false;
            return true;
        }
        if (source is ICollection col) {
            if (col.Count <= 0)
                return false;
            return true;
        }
        if (source.TryGetNonEnumeratedCount(out nb)) {
            if (nb <= 0)
                return false;
            return true;
        }
        var e = source.GetEnumerator();
        if (e.MoveNext()) {
            value = new PeekableWrapper(e.Current, e);
            return true;
        }
        (e as IDisposable)?.Dispose();
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand(IDbCommand cmd, object? parameterObj, Span<bool> usageMap) {
        if (parameterObj is null)
            return ActualSetCommand(cmd, new NoTypeAccessor(), usageMap);
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        var cache = GetAccessorCache(handle, type);
        return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand(DbCommand cmd, object? parameterObj, Span<bool> usageMap) {
        if (parameterObj is null)
            return ActualSetCommand(cmd, new NoTypeAccessor(), usageMap);
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        var cache = GetAccessorCache(handle, type);
        return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        return ActualSetCommand(cmd, new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        return ActualSetCommand(cmd, new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        return ActualSetCommand(cmd, new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var cache = GetAccessorCache(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return ActualSetCommand(cmd, new TypeAccessor(parameterObj, cache.GetUsage, cache.GetValue), usageMap);
        var c = Unsafe.As<TypeAccessorCache, StructTypeAccessorCache<T>>(ref cache);
        return ActualSetCommand(cmd, new TypeAccessor<T>(ref parameterObj, c.GenericGetUsage, c.GenericGetValue), usageMap);
    }
    /// <summary>
    /// Unsafe getter to get the cached accessor
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TypeAccessorCache GetAccessorCache(IntPtr handle, Type type) {
        var hds = _handles;
        var funcs = _funcs;
        for (int i = 0; i < hds.Length; i++)
            if (hds[i] == handle)
                return funcs[i];
        lock (TypeAccessorSharedLock) {
            for (int i = 0; i < _handles.Length; i++)
                if (_handles[i] == handle)
                    return _funcs[i];
            var method = typeof(TypeAccessorCacher<>).MakeGenericType(type).GetMethod(nameof(TypeAccessorCacher<>.GetOrGenerate), BindingFlags.Public | BindingFlags.Static);
            var res = (TypeAccessorCache)method!.Invoke(null, [Mapper])!;
            int len = _handles.Length;
            var newH = new IntPtr[len + 1];
            var newF = new TypeAccessorCache[len + 1];
            _handles.CopyTo(newH, 0);
            _funcs.CopyTo(newF, 0);
            newH[len] = handle;
            newF[len] = res;
            _handles = newH;
            _funcs = newF;
            return res;
        }
    }
#if NET9_0_OR_GREATER
    private bool ActualSetCommand<T>(IDbCommand cmd, T accessor, Span<bool> usageMap) where T : ITypeAccessor, allows ref struct {
#else
    private bool ActualSetCommand(IDbCommand cmd, NoTypeAccessor accessor, Span<bool> usageMap) {
#endif
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
#if NET9_0_OR_GREATER
    private bool ActualSetCommand<T>(DbCommand cmd, T accessor, Span<bool> usageMap) where T : ITypeAccessor, allows ref struct {
#else
    private bool ActualSetCommand(DbCommand cmd, NoTypeAccessor accessor, Span<bool> usageMap) {
#endif
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
#if !NET9_0_OR_GREATER
    private bool ActualSetCommand(IDbCommand cmd, TypeAccessor accessor, Span<bool> usageMap) {
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
    private bool ActualSetCommand(DbCommand cmd, TypeAccessor accessor, Span<bool> usageMap) {
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
    private bool ActualSetCommand<T>(IDbCommand cmd, TypeAccessor<T> accessor, Span<bool> usageMap) {
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
    private bool ActualSetCommand<T>(DbCommand cmd, TypeAccessor<T> accessor, Span<bool> usageMap) {
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
#endif
    }

internal class PeekableWrapper(object? first, IEnumerator enumerator) : IEnumerable<object>, IDisposable {
    private object? _first = first;
    private IEnumerator? _enumerator = enumerator;

    public IEnumerator<object> GetEnumerator() {
        if (_enumerator == null)
            yield break;

        yield return _first!;
        _first = null;

        while (_enumerator.MoveNext())
            yield return _enumerator.Current;
        Dispose();
    }
    public void Dispose() {
        if (_enumerator is not null) {
            (_enumerator as IDisposable)?.Dispose();
            _enumerator = null;
            _first = null;
        }
        GC.SuppressFinalize(this);
    }
    ~PeekableWrapper() => Dispose();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
