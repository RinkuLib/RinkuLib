using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

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
public class QueryCommand : ParsingCache, IQueryCommand, ICache {
    /// <inheritdoc/>
    public Mapper Mapper;
    Mapper IQueryCommand.Mapper => Mapper;
    int IQueryCommand.StartBaseHandlers => StartBaseHandlers;
    int IQueryCommand.StartSpecialHandlers => StartSpecialHandlers;
    int IQueryCommand.StartVariables => StartVariables;
    int IQueryCommand.EndSelect => EndSelect;
    /// <summary> The registry for parameter metadata and caching strategies. </summary>
    public readonly QueryParameters Parameters;
    /// <summary> The SQL template and segment parsing logic. </summary>
    public readonly QueryText QueryText;
    private object? _parsingCache;
    private IntPtr[] _handles = [];
    private (Func<IntPtr, int, bool> Usage, Func<IntPtr, int, object> Value)[] _funcs = [];
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
    /// <inheritdoc/>
    public readonly int StartBaseHandlers;
    /// <inheritdoc/>
    public readonly int StartSpecialHandlers;
    /// <inheritdoc/>
    public readonly int StartVariables;
    /// <inheritdoc/>
    public readonly int EndSelect;
    /// <summary>Initialization of a query command from a SQL query template</summary>
    public QueryCommand(string query, char variableChar = default)
        : this(new QueryFactory(query, variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
    /// <summary>The direct call the the constructor</summary>
    protected QueryCommand(QueryFactory factory) {
        Mapper = factory.Mapper;
        var segments = factory.Segments;
        var queryString = factory.Query;
        StartSpecialHandlers = Mapper.Count - factory.NbHandlers;
        StartBaseHandlers = Mapper.Count - factory.NbBaseHandlers;
        StartVariables = StartSpecialHandlers - factory.NbNormalVar;
        EndSelect = factory.NbSelects;
        var specialHandlers = GetHandlers(queryString, segments);
        QueryText = new(queryString, segments, factory.Conditions);
        Parameters = new(StartSpecialHandlers - StartVariables, specialHandlers);
        _parsingCache = ParsingCache.New(factory.NbSelects);
    }
    /// <inheritdoc/>
    public override bool TryGetCache<T>(Span<bool> usageMap, out SchemaParser<T> cache) {
        if (_parsingCache is not SchemaParser<T> c) {
            if (_parsingCache is ParsingCache p)
                p.TryGetCache(usageMap, out c);
            else {
                cache = default;
                return false;
            }
        }
        cache = c;
        return !NeedToCache(usageMap);
    }

    /// <inheritdoc/>
    public override int GetActualCacheIndex<T>(Span<bool> usageMap) {
        if (_parsingCache is ParsingCache p)
            return p.GetActualCacheIndex<T>(usageMap);
        if (_parsingCache is null) {
            _parsingCache = new SchemaParser<T>();
            return 0;
        }
        if (_parsingCache is not SchemaParser<T>)
            return -1;
        return 0;
    }
    /// <inheritdoc/>
    public override bool TryGetCache<T>(object?[] variables, out SchemaParser<T> cache) {
        if (_parsingCache is not SchemaParser<T> c) {
            if (_parsingCache is ParsingCache p)
                p.TryGetCache(variables, out c);
            else {
                cache = default;
                return false;
            }
        }
        cache = c;
        return !NeedToCache(variables);
    }

    /// <inheritdoc/>
    public override int GetActualCacheIndex<T>(object?[] variables) {
        if (_parsingCache is ParsingCache p)
            return p.GetActualCacheIndex<T>(variables);
        if (_parsingCache is null) {
            _parsingCache = new SchemaParser<T>();
            return 0;
        }
        if (_parsingCache is not SchemaParser<T>)
            return -1;
        return 0;
    }
    /// <inheritdoc/>
    public override bool UpdateCache<T>(int index, SchemaParser<T> cache) {
        if (_parsingCache is ParsingCache p)
            return p.UpdateCache(index, cache);
        if (_parsingCache is null) {
            _parsingCache = cache;
            return true;
        }
        if (_parsingCache is not SchemaParser<T>)
            return false;
        if (index != 0)
            return false;
        _parsingCache = cache;
        return true;
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
            var ind = Mapper.GetIndex(item.Key) - StartVariables;
            if (ind < 0 || Parameters.IsCached(ind))
                continue;
            Parameters.UpdateCache(ind, infoGetter.MakeInfoAt(item.Value));
        }
        Parameters.UpdateSpecialHandlers(infoGetter);
        Parameters.UpdateNbCached();
        return true;
    }
    /// <summary>
    /// Provide a manual way to set a cache for a specific parameter
    /// </summary>
    public bool UpdateParamCache(string paramName, DbParamInfo paramInfo) {
        var ind = Mapper.GetIndex(paramName) - StartVariables;
        if (ind < 0)
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
        var nbVariables = Parameters.NbVariables;

        ref object? pVar = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(variables), StartVariables);
        ref string pKeys = ref Mapper.KeysStartPtr;

        for (int i = 0; i < nbVariables; i++) {
            var currentVar = Unsafe.Add(ref pVar, i);
            if (currentVar is not null)
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, currentVar);
        }

        ref object? pSpecialVar = ref Unsafe.Add(ref pVar, nbVariables);
        var nbSpecialHandlers = Parameters.Total - nbVariables;

        for (int i = 0; i < nbSpecialHandlers; i++) {
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
        var nbVariables = Parameters.NbVariables;

        ref object? pVar = ref Unsafe.Add(ref MemoryMarshal.GetReference(variables), StartVariables);
        ref string pKeys = ref Unsafe.Add(ref Mapper.KeysStartPtr, StartVariables);

        for (int i = 0; i < nbVariables; i++) {
            var currentVar = Unsafe.Add(ref pVar, i);
            if (currentVar is not null)
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, currentVar);
        }

        ref object? pSpecialVar = ref Unsafe.Add(ref pVar, nbVariables);
        var nbSpecialHandlers = Parameters.Total - nbVariables;

        for (int i = 0; i < nbSpecialHandlers; i++) {
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
        IntPtr handle = *(IntPtr*)Unsafe.AsPointer(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(parameterObj).Data) {
            return SetCommand(cmd, GetAccessor(ptr, handle, parameterObj.GetType()), usageMap);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand(DbCommand cmd, object parameterObj, Span<bool> usageMap) {
        IntPtr handle = *(IntPtr*)Unsafe.AsPointer(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(parameterObj).Data) {
            return SetCommand(cmd, GetAccessor(ptr, handle, parameterObj.GetType()), usageMap);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        object obj = Unsafe.As<T, object>(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(obj).Data)
            return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        object obj = Unsafe.As<T, object>(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(obj).Data)
            return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        object obj = Unsafe.As<T, object>(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(obj).Data)
            return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull { 
        IntPtr handle = typeof(T).TypeHandle.Value;
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            return SetCommand(cmd, GetAccessor(Unsafe.AsPointer(ref parameterObj), handle, typeof(T)), usageMap);
        object obj = Unsafe.As<T, object>(ref parameterObj);
        fixed (void* ptr = &Unsafe.As<RawData>(obj).Data)
            return SetCommand(cmd, GetAccessor(ptr, handle, typeof(T)), usageMap);
    }
    [StructLayout(LayoutKind.Sequential)]
    private class RawData { public byte Data; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe TypeAccessor GetAccessor(void* ptr, IntPtr handle, Type type) {
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

            var method = typeof(TypeAccessor<>).MakeGenericType(type).GetMethod("GetOrGenerate", BindingFlags.Public | BindingFlags.Static);
            var res = ((Func<IntPtr, int, bool>, Func<IntPtr, int, object>))method!.Invoke(null, [StartVariables, Mapper])!;
            int len = _handles.Length;
            var newH = new IntPtr[len + 1];
            var newF = new (Func<IntPtr, int, bool>, Func<IntPtr, int, object>)[len + 1];
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
        var nbVariables = Parameters.NbVariables;

        ref string pKeys = ref Unsafe.Add(ref Mapper.KeysStartPtr, StartVariables);
        var startVariables = StartVariables;
        var nbSpecialHandlers = Parameters.Total - nbVariables;
        var total = Mapper.Count;
        int i = 0;
        for (; i < startVariables; i++)
            usageMap[i] = accessor.IsUsed(i);

        for (; i < nbVariables; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, accessor.GetValue(i));

        for (; i < nbSpecialHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlers[i].Use(cmd, accessor.GetValue(i));

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        cmd.CommandText = QueryText.Parse(usageMap, accessor);
        return true;
    }
    private bool SetCommand(DbCommand cmd, TypeAccessor accessor, Span<bool> usageMap) {
        throw new NotImplementedException();
    }
}
