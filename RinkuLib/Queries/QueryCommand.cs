using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    // One entry exists for each source type. It starts as the one accessor the command actually used and is
    // promoted to an AccessorPair only when that exact type later needs the other path too.
    private (IntPtr Handle, object Accessor)[] _accessors = [];
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
            var condStates = entry.CondStates;
            int idxLen = condStates.Length;
            ref int pBase = ref MemoryMarshal.GetArrayDataReference(condStates);
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
            if (entry.ResultSetIndex != resultSetIndex)
                goto NextEntry;
            var condStates = entry.CondStates;
            int idxLen = condStates.Length;
            ref int pBase = ref MemoryMarshal.GetArrayDataReference(condStates);
            for (int j = 0; j < idxLen; j++) {
                int packed = Unsafe.Add(ref pBase, j);
                if ((Unsafe.Add(ref usageBase, packed >> 1) is not null) != ((packed & 1) != 0))
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
        if (parameterObj is null) {
            usageMap.Clear();
            SetText(cmd, QueryText.Parse(usageMap, EmptyHandlerValues()));
            return true;
        }
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, type);
        return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand(DbCommand cmd, object? parameterObj, Span<bool> usageMap) {
        if (parameterObj is null) {
            usageMap.Clear();
            SetText(cmd, QueryText.Parse(usageMap, EmptyHandlerValues()));
            return true;
        }
        var type = parameterObj.GetType();
        IntPtr handle = type.TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, type);
        return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(IDbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj!, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj!, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj!, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        IntPtr handle = typeof(T).TypeHandle.Value;
        var accessor = GetDirectAccessor(handle, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj!, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
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
    public DirectAccessor GetDirectAccessor(IntPtr handle, Type type) {
        var accessors = Volatile.Read(ref _accessors);
        for (int i = 0; i < accessors.Length; i++)
            if (accessors[i].Handle == handle) {
                if (accessors[i].Accessor is DirectAccessor direct)
                    return direct;
                if (accessors[i].Accessor is AccessorPair pair)
                    return pair.Direct;
                break;
            }
        lock (TypeAccessorSharedLock) {
            accessors = _accessors;
            for (int i = 0; i < accessors.Length; i++)
                if (accessors[i].Handle == handle) {
                    if (accessors[i].Accessor is DirectAccessor direct)
                        return direct;
                    if (accessors[i].Accessor is AccessorPair pair)
                        return pair.Direct;

                    var createdDirect = ParameterAccessorGenerator.CreateDirect(type, Mapper, Parameters._specialHandlers,
                        StartSpecialHandlers, StartBoolCond);
                    var updated = new (IntPtr Handle, object Accessor)[accessors.Length];
                    Array.Copy(accessors, updated, accessors.Length);
                    updated[i] = (handle, new AccessorPair(createdDirect, (UseWithAccessor)accessors[i].Accessor));
                    Volatile.Write(ref _accessors, updated);
                    return createdDirect;
                }
            var accessor = ParameterAccessorGenerator.CreateDirect(type, Mapper, Parameters._specialHandlers,
                StartSpecialHandlers, StartBoolCond);
            Volatile.Write(ref _accessors, [.. accessors, (handle, accessor)]);
            return accessor;
        }
    }

    /// <summary>Gets the independent accessor cache used by <c>UseWith</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UseWithAccessor GetUseWithAccessor(IntPtr handle, Type type) {
        var accessors = Volatile.Read(ref _accessors);
        for (int i = 0; i < accessors.Length; i++)
            if (accessors[i].Handle == handle) {
                if (accessors[i].Accessor is UseWithAccessor useWith)
                    return useWith;
                if (accessors[i].Accessor is AccessorPair pair)
                    return pair.UseWith;
                break;
            }
        lock (TypeAccessorSharedLock) {
            accessors = _accessors;
            for (int i = 0; i < accessors.Length; i++)
                if (accessors[i].Handle == handle) {
                    if (accessors[i].Accessor is UseWithAccessor useWith)
                        return useWith;
                    if (accessors[i].Accessor is AccessorPair pair)
                        return pair.UseWith;

                    var createdUseWith = ParameterAccessorGenerator.CreateUseWith(type, Mapper, Parameters._specialHandlers,
                        StartSpecialHandlers, StartBoolCond);
                    var updated = new (IntPtr Handle, object Accessor)[accessors.Length];
                    Array.Copy(accessors, updated, accessors.Length);
                    updated[i] = (handle, new AccessorPair((DirectAccessor)accessors[i].Accessor, createdUseWith));
                    Volatile.Write(ref _accessors, updated);
                    return createdUseWith;
                }
            var accessor = ParameterAccessorGenerator.CreateUseWith(type, Mapper, Parameters._specialHandlers,
                StartSpecialHandlers, StartBoolCond);
            Volatile.Write(ref _accessors, [.. accessors, (handle, accessor)]);
            return accessor;
        }
    }

    private sealed class AccessorPair(DirectAccessor direct, UseWithAccessor useWith) {
        internal readonly DirectAccessor Direct = direct;
        internal readonly UseWithAccessor UseWith = useWith;
    }
    private bool FinishSetCommand(IDbCommand cmd, object?[] handlerValues, Span<bool> usageMap) {
        var handlers = Parameters._specialHandlers;
        for (int i = StartSpecialHandlers; i < StartBaseHandlers; i++) {
            if (!usageMap[i])
                continue;
            ref object? value = ref handlerValues[i - StartSpecialHandlers];
            var handler = handlers[i - StartSpecialHandlers];
            if (!handler.CanHandle(ref value)) {
                usageMap[i] = false;
                continue;
            }
            handler.Use(cmd, ref value);
        }
        SetText(cmd, QueryText.Parse(usageMap, handlerValues));
        return true;
    }
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
