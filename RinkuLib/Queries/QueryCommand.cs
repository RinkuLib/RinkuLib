using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.Queries;
/// <summary>
/// A query defined once from a SQL template and reused for the life of the app. It holds no per-call state,
/// so one instance is safe to share across threads, the values for each run travel in the call. Declare it
/// in a <see langword="static readonly"/> field and run it with the execution methods (<c>Query</c>,
/// <c>Execute</c>, and the rest), or open a <see cref="Commands.QueryBuilder"/> on it to set values from code.
/// </summary>
/// <remarks>
/// The template can mark parts optional, so the values a run supplies decide the final SQL. It also learns a
/// provider's parameter metadata and a result's row parser on first use and reuses them, so a warm command
/// runs without rediscovering either.
/// </remarks>
public class QueryCommand : IQueryCommand, ICache {
    /// <inheritdoc/>
    public readonly Mapper Mapper;
    Mapper IQueryCommand.Mapper => Mapper;
    int IQueryCommand.StartBaseHandlers => StartBaseHandlers;
    int IQueryCommand.StartSpecialHandlers => StartSpecialHandlers;
    int IQueryCommand.StartBoolCond => StartBoolCond;
    /// <summary> How each parameter is bound, and the learned provider metadata behind it. </summary>
    public readonly QueryParameters Parameters;
    /// <summary> The template, and the rendering of it down to the SQL a run sends. </summary>
    public readonly QueryText QueryText;
    /// <summary> The row parsers learned so far, one per result shape seen, reused across runs. </summary>
    public ParsingCacheItem[] ParsingCache = [];
    private IntPtr[] _handles = [];
    private TypeAccessorCache[] _funcs = [];
    /// <summary>
    /// Guards the shared accessor cache while it learns how to read a new parameter object type.
    /// </summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        TypeAccessorSharedLock = new();
    /// <summary>
    /// Guards the shared parser cache while it learns the row parser for a new result shape.
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
    /// <summary>
    /// Defines a command from a SQL template. The template is read once, here, and the command is then reused
    /// for every run.
    /// </summary>
    /// <param name="query">The SQL, optionally carrying conditional markers.</param>
    /// <param name="variableChar">The character that marks a variable, <c>@</c> when left unset.</param>
    public QueryCommand(string query, char variableChar = default)
        : this(new QueryFactory(query, variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
    /// <summary>Defines a command from an already-parsed template, the extension point a subclass builds on.</summary>
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
    /// Looks up the row parser already learned for this run's shape, so a warm command can read the result
    /// without inspecting the columns again. Returns <see langword="false"/> when nothing is cached yet.
    /// </summary>
    public bool TryGetCachedParser<T>(Span<bool> usageMap, [MaybeNullWhen(false)] out ITypeParser<T> parser, int resultSetIndex = 0) {
        ref bool pUsage = ref MemoryMarshal.GetReference(usageMap);
        var cacheArray = ParsingCache;
        int cacheLen = cacheArray.Length;

        for (int i = 0; i < cacheLen; i++) {
            ref var entry = ref cacheArray[i];
            if (entry.ResultSetIndex != resultSetIndex)
                goto NextEntry;
            int idxLen = entry.CondStates.Length;
            ref int pBase = ref MemoryMarshal.GetReference(entry.CondStates);
            for (int j = 0; j < idxLen; j++) {
                int packed = Unsafe.Add(ref pBase, j);
                if (Unsafe.Add(ref pUsage, packed >> 1) != ((packed & 1) != 0))
                    goto NextEntry;
            }
            parser = entry.Parser as ITypeParser<T>;
            if (parser is not null)
                return !NeedToCache(usageMap);
        NextEntry:
            ;
        }
        parser = default;
        return false;
    }
    /// <summary>
    /// Looks up the row parser already learned for this run's shape, so a warm command can read the result
    /// without inspecting the columns again. Returns <see langword="false"/> when nothing is cached yet.
    /// </summary>
    public bool TryGetCachedParser<T>(object?[] usageMap, [MaybeNullWhen(false)] out ITypeParser<T> parser, int resultSetIndex = 0) {
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

            parser = entry.Parser as ITypeParser<T>;
            if (entry.ResultSetIndex != resultSetIndex)
                return false;
            if (parser is not null)
                return !NeedToCache(usageMap);

        NextEntry:
            ;
        }

        parser = default;
        return false;
    }
    /// <summary>
    /// Records the row parser learned for a result's columns so later runs of the same shape reuse it.
    /// </summary>
    public void UpdateParseCache<T>(bool[] usageMap, ColumnInfo[] schema, ITypeParser<T> cache, int resultSetIndex = 0) {
        lock (ParsingCacheSharedLock) { 
            ParsingCache = ParsingCache.GetUpdatedCache(QueryText, usageMap, schema, cache, resultSetIndex);
        }
    }
    /// <summary>
    /// Whether this run touches a parameter whose provider metadata has not been learned yet, the signal that
    /// the command still has caching to do on this pass.
    /// </summary>
    /// <returns><see langword="false"/> when every used parameter is already cached.</returns>
    public bool NeedToCache(Span<bool> usageMap)
        => Parameters.NeedToCache(usageMap);
    /// <summary>
    /// Whether this run touches a parameter whose provider metadata has not been learned yet, the signal that
    /// the command still has caching to do on this pass.
    /// </summary>
    /// <returns><see langword="false"/> when every used parameter is already cached.</returns>
    public bool NeedToCache(object?[] variables)
        => Parameters.NeedToCache(variables);
    /// <summary>
    /// Learns how this command's parameters should be bound from a live command that has just run, so later
    /// runs bind them the same way without the guesswork. Prefers a provider-specific reader when one is
    /// registered in <see cref="IDbParamInfoGetter.ParamGetterMakers"/>, otherwise reads the parameters as-is.
    /// </summary>
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
    /// <inheritdoc/>
    public Task UpdateCacheAsync(IDbCommand cmd, CancellationToken ct = default) {
        UpdateCache(cmd);
        return Task.CompletedTask;
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
    /// Sets how one parameter is bound by hand, in place of letting the command learn it from a run. Use this
    /// to pin a type, size, or provider quirk the automatic path would get wrong.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="paramName"/> names a bindable parameter.</returns>
    public bool UpdateParamCache(string paramName, DbParamInfo paramInfo) {
        var ind = Mapper.GetIndex(paramName);
        if (ind < 0 || ind >= StartBaseHandlers)
            return false;
        return Parameters.UpdateCache(ind, paramInfo);
    }
    /// <inheritdoc/>
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
        // a lazy sequence has no reusable count, so it is materialized once here: the binding pass and the
        // render pass both need to walk it
        var e = source.GetEnumerator();
        try {
            if (!e.MoveNext())
                return false;
            var items = new List<object?> { e.Current };
            while (e.MoveNext())
                items.Add(e.Current);
            value = items.ToArray();
            return true;
        }
        finally {
            (e as IDisposable)?.Dispose();
        }
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
    /// The cached plan for reading a parameter object of the given type, its members mapped to this command's
    /// keys. Built on first sight of the type and reused after, so binding a familiar object type is cheap.
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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

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
                handlers[i - StartSpecialHandlers].Use(cmd, accessor.GetValue(i));

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        cmd.CommandText = QueryText.Parse(usageMap, accessor);
        return true;
    }
#endif
}

