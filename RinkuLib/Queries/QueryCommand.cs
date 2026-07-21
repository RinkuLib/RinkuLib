using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
    /// How the provider reads this command's text. <see cref="System.Data.CommandType.Text"/> for SQL, which
    /// is what a template is, and <see cref="System.Data.CommandType.StoredProcedure"/> for a command whose
    /// text names a procedure.
    /// </summary>
    public readonly CommandType CommandType;
    /// <summary>
    /// Puts the run's text on the command, and the reading it needs when that is not the provider's default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetText(IDbCommand cmd, string text) {
        cmd.CommandText = text;
        if (CommandType != CommandType.Text)
            cmd.CommandType = CommandType;
    }
    /// <summary>
    /// Defines a command from a SQL template. The template is read once, here, and the command is then reused
    /// for every run.
    /// </summary>
    /// <param name="query">The SQL, optionally carrying conditional markers.</param>
    /// <param name="variableChar">The character that marks a variable, <c>@</c> when left unset.</param>
    public QueryCommand(string query, char variableChar = default)
        : this(new QueryFactory(query, variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
    /// <summary>
    /// Defines a command whose parameters are named rather than read out of its text, and says how the
    /// provider should read the text. A stored procedure is the case this exists for: the text is the
    /// procedure's name, which carries no variables to find, so the parameters are given instead.
    /// </summary>
    /// <param name="commandText">The text to send, used exactly as given, with no markers read from it.</param>
    /// <param name="variableNames">
    /// The parameters to bind, in order. Each is required, so a run supplies them all. A name may be written
    /// with or without the variable character.
    /// </param>
    /// <param name="commandType">How the provider reads the text.</param>
    /// <example>
    /// <code>
    /// static readonly QueryCommand Renumber =
    ///     new("dbo.RenumberTracks", ["albumId", "moved"], CommandType.StoredProcedure);
    ///
    /// Renumber.Execute(cnn, new { albumId = 1, moved = 0 });
    /// </code>
    /// </example>
    public QueryCommand(string commandText, IEnumerable<string> variableNames, CommandType commandType = CommandType.StoredProcedure)
        : this(new QueryFactory(commandText, variableNames), commandType) { }
    /// <summary>Defines a command from an already-parsed template, the extension point a subclass builds on.</summary>
    protected QueryCommand(QueryFactory factory) : this(factory, CommandType.Text) { }
    /// <summary>
    /// Defines a command from an already-parsed template, saying how the provider should read the text.
    /// </summary>
    /// <param name="factory">The template already read into its pieces.</param>
    /// <param name="commandType">How the provider reads the text.</param>
    protected QueryCommand(QueryFactory factory, CommandType commandType) {
        CommandType = commandType;
        Mapper = factory.Mapper;
        var segments = factory.Segments;
        var queryString = factory.Query;
        StartBoolCond = Mapper.Count - factory.NbNonVarComment;
        StartBaseHandlers = StartBoolCond - factory.NbBaseHandlers;
        StartSpecialHandlers = StartBaseHandlers - factory.NbSpecialHandlers;
        var specialHandlers = SpecialHandler.GetHandlers(StartSpecialHandlers, StartBaseHandlers, Mapper, queryString, segments);
        QueryText = QueryText.Create(queryString, segments, factory.Conditions, StartSpecialHandlers, StartBoolCond - StartSpecialHandlers);
        Parameters = new(factory.NbNormalVar, specialHandlers);
    }
    /// <summary>
    /// A command for a stored procedure, read from the database. What the procedure declares is what the
    /// command binds, so the names, their types, their sizes and their directions all come from the one
    /// place that knows them.
    /// </summary>
    /// <param name="procedureName">The procedure to call.</param>
    /// <param name="connection">The connection to ask, opened for the question if it is not already.</param>
    /// <remarks>
    /// Asking costs a round trip, so this belongs where a command is built, once, and not in a call. Without
    /// a connection to ask, name the parameters yourself with
    /// <see cref="QueryCommand(string, IEnumerable{string}, CommandType)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// static readonly QueryCommand Renumber = QueryCommand.FromProc("dbo.RenumberTracks", cnn);
    ///
    /// Renumber.Execute(cnn, new { albumId = 1, moved = 0 });
    /// </code>
    /// </example>
    public static QueryCommand FromProc(string procedureName, IDbConnection connection)
        => StoredProcedure.From(connection, procedureName);
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
        ref SpecialHandler pHandlers = ref MemoryMarshal.GetArrayDataReference(handlers);
        for (int i = 0; i < handlers.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is null)
                continue;
            var handler = Unsafe.Add(ref pHandlers, i);
            if (!handler.CanHandle(ref currentVar)) {
                currentVar = null;
                continue;
            }
            handler.Use(cmd, ref currentVar);
        }

        SetText(cmd, QueryText.Parse(variables));

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
        ref SpecialHandler pHandlers = ref MemoryMarshal.GetArrayDataReference(handlers);
        for (int i = 0; i < handlers.Length; i++) {
            ref var currentVar = ref Unsafe.Add(ref pSpecialVar, i);
            if (currentVar is null)
                continue;
            var handler = Unsafe.Add(ref pHandlers, i);
            if (!handler.CanHandle(ref currentVar)) {
                currentVar = null;
                continue;
            }
            handler.Use(cmd, ref currentVar);
        }

        SetText(cmd, QueryText.Parse(variables));

        return true;
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
    /// A run that supplies nothing still has to answer for the handler spots the template keeps, so the slots
    /// are there and empty rather than absent, and a spot that needed one is refused by name.
    /// </summary>
    private Span<object?> EmptyHandlerValues()
        => QueryText.HandlerValuesLength <= 0 ? default : new object?[QueryText.HandlerValuesLength];
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
            TypeAccessorCache res;
            try {
                res = (TypeAccessorCache)method!.Invoke(null, [Mapper, Parameters._specialHandlers, StartSpecialHandlers])!;
            }
            catch (TargetInvocationException e) when (e.InnerException is not null) {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
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
        Debug.Assert(usageMap.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref string pKeys = ref Mapper.KeysStartPtr;
        var total = Mapper.Count;
        int i = 0;
        for (; i < StartSpecialHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, accessor.GetValue(i));

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
        return true;
    }
    private bool ActualSetCommand<T>(DbCommand cmd, T accessor, Span<bool> usageMap) where T : ITypeAccessor, allows ref struct {
        Debug.Assert(usageMap.Length == Mapper.Count);
        var varInfos = Parameters._variablesInfo;
        var handlers = Parameters._specialHandlers;

        ref string pKeys = ref Mapper.KeysStartPtr;
        var total = Mapper.Count;
        int i = 0;
        for (; i < StartSpecialHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                varInfos[i].Use(Unsafe.Add(ref pKeys, i), cmd, accessor.GetValue(i));

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
        return true;
    }
#else
    private bool ActualSetCommand(IDbCommand cmd, NoTypeAccessor accessor, Span<bool> usageMap) {
        Debug.Assert(usageMap.Length == Mapper.Count);
        usageMap.Clear();
        SetText(cmd, QueryText.Parse(usageMap, EmptyHandlerValues()));
        return true;
    }
    private bool ActualSetCommand(DbCommand cmd, NoTypeAccessor accessor, Span<bool> usageMap) {
        Debug.Assert(usageMap.Length == Mapper.Count);
        usageMap.Clear();
        SetText(cmd, QueryText.Parse(usageMap, EmptyHandlerValues()));
        return true;
    }
#endif
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

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
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

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
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

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
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

        var handlerValues = QueryText.HandlerValuesLength <= 0
            ? default
            : new object?[QueryText.HandlerValuesLength].AsSpan();

        for (; i < StartBaseHandlers; i++)
            if (usageMap[i] = accessor.IsUsed(i)) {
                var handled = accessor.GetValue(i);
                var handler = handlers[i - StartSpecialHandlers];
                if (!handler.CanHandle(ref handled)) {
                    usageMap[i] = false;
                    continue;
                }
                handler.Use(cmd, ref handled);
                handlerValues[i - StartSpecialHandlers] = handled;
            }

        for (; i < StartBoolCond; i++)
            if (usageMap[i] = accessor.IsUsed(i))
                handlerValues[i - StartSpecialHandlers] = accessor.GetValue(i);

        for (; i < total; i++)
            usageMap[i] = accessor.IsUsed(i);

        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
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

