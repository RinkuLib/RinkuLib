using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinku.Internal;
using Rinku.Mapping;
using Rinku.Mapping.Parsers;
using Rinku.Querying;
using Rinku.Querying.Defaults;

namespace Rinku;

/// <summary>Selects parameter accessors held by a command.</summary>
[Flags]
public enum ParameterAccessorKinds : byte {
    /// <summary>Selects no accessors.</summary>
    None = 0,
    /// <summary>Selects accessors used when a parameter object is passed to a query method.</summary>
    Direct = 1,
    /// <summary>Selects accessors used by <c>UseWith</c> on a <see cref="QueryBuilder"/>.</summary>
    UseWith = 2,
    /// <summary>Selects both kinds of parameter accessor.</summary>
    Both = Direct | UseWith
}

/// <summary>Selects where parser invalidation applies.</summary>
public enum QueryParserInvalidationScope : byte {
    /// <summary>Removes parsers only from this command.</summary>
    Local = 0,
    /// <summary>Removes the parsers from the global cache and from commands that use them.</summary>
    Global = 1,
    /// <summary>Removes a parser from the global cache when no other command uses it.</summary>
    GlobalIfUnused = 2
}

/// <summary>
/// A reusable SQL query or stored procedure.
/// Declare one in a <see langword="static readonly"/> field and call <c>Query</c> or <c>Execute</c> on it.
/// Use <see cref="QueryBuilder"/> when values are supplied in several steps.
/// </summary>
/// <remarks>
/// One instance can be shared across threads. Dispose it when it has a shorter lifetime than the application.
/// </remarks>
public class QueryCommand : IQueryCommand, ICache, IDisposable {
    private bool _subscribedToParserDisposing;
    private int _disposed;
    /// <inheritdoc/>
    public readonly Mapper Mapper;
    Mapper IQueryCommand.Mapper => Mapper;
    int IQueryCommand.StartBaseHandlers => StartBaseHandlers;
    int IQueryCommand.StartSpecialHandlers => StartSpecialHandlers;
    int IQueryCommand.StartBoolCond => StartBoolCond;
    /// <summary>Gets the parameter settings used by this command.</summary>
    public readonly QueryParameters Parameters;
    /// <summary>Gets the SQL template used by this command.</summary>
    public readonly QueryText QueryText;
    internal ParsingCacheItem[] ParsingCache = [];
    private (RuntimeTypeHandle Handle, object Accessor)[] _accessors = [];
    internal static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        TypeAccessorSharedLock = new();
    internal static readonly
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetText(IDbCommand cmd, string text) {
        var current = cmd.CommandText;
        if (!ReferenceEquals(current, text) && !string.Equals(current, text, StringComparison.Ordinal))
            cmd.CommandText = text;
        if (CommandType != CommandType.Text && cmd.CommandType != CommandType)
            cmd.CommandType = CommandType;
    }
    /// <summary>
    /// Defines a reusable command from a SQL template.
    /// </summary>
    /// <param name="query">The SQL, optionally carrying conditional markers.</param>
    /// <param name="variableChar">The character that marks a variable, <c>@</c> when left unset.</param>
    public QueryCommand(string query, char variableChar = default)
        : this(new QueryFactory(query, variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
    /// <summary>
    /// Defines a command with an explicit list of parameters.
    /// Use this overload for a stored procedure or for SQL that binds parameters by position.
    /// </summary>
    /// <param name="commandText">The SQL text or stored procedure name.</param>
    /// <param name="variableNames">
    /// The parameter names in binding order. Each parameter is required.
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

    /// <summary>Defines a command from a custom <see cref="QueryFactory"/>.</summary>
    protected QueryCommand(QueryFactory factory) : this(factory, CommandType.Text) { }
    /// <summary>
    /// Defines a command from a custom <see cref="QueryFactory"/> and command type.
    /// </summary>
    /// <param name="factory">
    /// The query factory to use. The command takes ownership of its mapper.
    /// </param>
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
    /// Creates a command for a stored procedure by reading its parameter details from the database.
    /// </summary>
    /// <param name="procedureName">The procedure to call.</param>
    /// <param name="connection">The connection to ask, opened for the question if it is not already.</param>
    /// <remarks>
    /// This call queries the database. Call it once when the command is declared.
    /// To avoid that query, name the parameters with
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
    /// Gets a parser held for the supplied parameter usage and result set.
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
    /// Gets a parser held for the supplied parameter values and result set.
    /// </summary>
    public bool TryGetCachedParser<T>(object?[] usageMap, [MaybeNullWhen(false)] out ITypeParser<T> parser, int resultSetIndex = 0) {
        if (Parameters.HasDefaultSet) {
            ref object? values = ref MemoryMarshal.GetArrayDataReference(usageMap);
            var defaultCache = ParsingCache;
            for (int i = 0; i < defaultCache.Length; i++) {
                ref var entry = ref defaultCache[i];
                if (entry.ResultSetIndex != resultSetIndex)
                    continue;
                var states = entry.CondStates;
                for (int j = 0; j < states.Length; j++) {
                    int packed = states[j];
                    bool used = Unsafe.Add(ref values, packed >> 1) is not null
                        || ((packed >> 1) < Parameters._variablesInfo.Length && Parameters._variablesInfo[packed >> 1].HasDefaultSet);
                    if (used != ((packed & 1) != 0))
                        goto NextDefaultEntry;
                }
                parser = entry.Parser as ITypeParser<T>;
                if (parser is not null)
                    return !NeedToCache(usageMap);
            NextDefaultEntry:;
            }
            parser = default;
            return false;
        }
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
    /// <summary>Gets a cached parser for a runtime result type and parameter usage.</summary>
    public bool TryGetCachedParser(Type type, Span<bool> usageMap, [MaybeNullWhen(false)] out ITypeParser parser, int resultSetIndex = 0) {
        ArgumentNullException.ThrowIfNull(type);
        ref bool usage = ref MemoryMarshal.GetReference(usageMap);
        var cache = ParsingCache;
        for (int i = 0; i < cache.Length; i++) {
            ref var entry = ref cache[i];
            if (entry.ResultSetIndex != resultSetIndex)
                continue;
            var states = entry.CondStates;
            for (int j = 0; j < states.Length; j++) {
                int packed = states[j];
                if (Unsafe.Add(ref usage, packed >> 1) != ((packed & 1) != 0))
                    goto NextEntry;
            }
            parser = entry.Parser;
            if (parser.Type == type)
                return !NeedToCache(usageMap);
        NextEntry:;
        }
        parser = default!;
        return false;
    }
    /// <summary>Gets a cached parser for a runtime result type and parameter values.</summary>
    public bool TryGetCachedParser(Type type, object?[] usageMap, [MaybeNullWhen(false)] out ITypeParser parser, int resultSetIndex = 0) {
        if (Parameters.HasDefaultSet) {
            var effective = CreateUsageMap(usageMap);
            return TryGetCachedParser(type, effective.AsSpan(), out parser, resultSetIndex);
        }
        ref object? values = ref MemoryMarshal.GetArrayDataReference(usageMap);
        var cache = ParsingCache;
        for (int i = 0; i < cache.Length; i++) {
            ref var entry = ref cache[i];
            if (entry.ResultSetIndex != resultSetIndex)
                continue;
            var states = entry.CondStates;
            for (int j = 0; j < states.Length; j++) {
                int packed = states[j];
                if ((Unsafe.Add(ref values, packed >> 1) is not null) != ((packed & 1) != 0))
                    goto NextEntry;
            }
            parser = entry.Parser;
            if (parser.Type == type)
                return !NeedToCache(usageMap);
        NextEntry:;
        }
        parser = default!;
        return false;
    }
    /// <summary>
    /// Stores a parser for the supplied parameter usage and result set.
    /// </summary>
    public void UpdateParseCache<T>(bool[] usageMap, ITypeParser<T> cache, int resultSetIndex = 0) {
        lock (TypeParser.TypeParserMakers) {
            if (!TypeParser.IsGloballyCached(cache))
                return;
            lock (ParsingCacheSharedLock) {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                if (!_subscribedToParserDisposing) {
                    TypeParser.ParserDisposing += OnParserDisposing;
                    _subscribedToParserDisposing = true;
                }
                ParsingCache = ParsingCache.GetUpdatedCache(QueryText, usageMap, cache, resultSetIndex);
            }
        }
    }
    /// <summary>Stores a runtime parser in this command's existing parser cache.</summary>
    public void UpdateParseCache(bool[] usageMap, ITypeParser cache, int resultSetIndex = 0) {
        lock (TypeParser.TypeParserMakers) {
            if (!TypeParser.IsGloballyCached(cache))
                return;
            lock (ParsingCacheSharedLock) {
                if (Volatile.Read(ref _disposed) != 0)
                    return;
                if (!_subscribedToParserDisposing) {
                    TypeParser.ParserDisposing += OnParserDisposing;
                    _subscribedToParserDisposing = true;
                }
                ParsingCache = ParsingCache.GetUpdatedCache(QueryText, usageMap, cache, resultSetIndex);
            }
        }
    }
    /// <summary>
    /// Removes every parser held by this command.
    /// A parser is also removed from the global cache when nothing else uses it.
    /// </summary>
    /// <returns>The number of cache entries removed.</returns>
    public int InvalidateParsers() => InvalidateParsers(QueryParserInvalidationScope.GlobalIfUnused);
    /// <summary>
    /// Removes every parser held by this command using the selected scope.
    /// </summary>
    /// <returns>The number of cache entries this command held when invalidation began.</returns>
    public int InvalidateParsers(QueryParserInvalidationScope scope) {
        ValidateParserInvalidationScope(scope);
        var count = TakeLocalParsers(out var parsers);
        for (int i = 0; i < parsers.Length; i++)
            InvalidateReleasedParser(parsers[i], scope);
        return count;
    }
    /// <summary>
    /// Removes one parser from this command.
    /// It is also removed from the global cache when nothing else uses it.
    /// </summary>
    /// <returns>The number of this command's cache entries that referenced <paramref name="parser"/>.</returns>
    public int InvalidateParser(ITypeParser parser)
        => InvalidateParser(parser, QueryParserInvalidationScope.GlobalIfUnused);
    /// <summary>
    /// Removes one parser from this command using the selected scope.
    /// </summary>
    /// <returns>The number of this command's cache entries that referenced <paramref name="parser"/>.</returns>
    public int InvalidateParser(ITypeParser parser, QueryParserInvalidationScope scope) {
        ArgumentNullException.ThrowIfNull(parser);
        ValidateParserInvalidationScope(scope);
        var count = TakeLocalParser(parser);
        if (count != 0)
            InvalidateReleasedParser(parser, scope);
        return count;
    }
    private static void ValidateParserInvalidationScope(QueryParserInvalidationScope scope) {
        if (scope != QueryParserInvalidationScope.Local && scope != QueryParserInvalidationScope.Global
            && scope != QueryParserInvalidationScope.GlobalIfUnused)
            throw new ArgumentOutOfRangeException(nameof(scope));
    }
    private static void InvalidateReleasedParser(ITypeParser parser, QueryParserInvalidationScope scope) {
        if (scope == QueryParserInvalidationScope.GlobalIfUnused) {
            if (!TypeParser.TryInvalidateIfUnreferenced(parser))
                TypeParser.TryDisposeParser(parser, ParserInvalidationMode.CheckUsage);
        }
        else if (scope == QueryParserInvalidationScope.Global) {
            if (!TypeParser.Invalidate(parser, ParserInvalidationMode.InvalidateReferences))
                TypeParser.TryDisposeParser(parser, ParserInvalidationMode.InvalidateReferences);
        }
        else
            TypeParser.TryDisposeParser(parser, ParserInvalidationMode.CheckUsage);
    }
    private int TakeLocalParsers(out ITypeParser[] parsers) {
        int count;
        lock (ParsingCacheSharedLock) {
            var current = ParsingCache;
            count = current.Length;
            if (count == 0) {
                UnsubscribeFromParserDisposing();
                parsers = [];
                return 0;
            }
            parsers = GetDistinctParsers(current);
            ParsingCache = [];
            UnsubscribeFromParserDisposing();
        }
        return count;
    }
    private int TakeLocalParser(ITypeParser parser) {
        lock (ParsingCacheSharedLock) {
            var current = ParsingCache;
            int removed = 0;
            for (int i = 0; i < current.Length; i++)
                if (ReferenceEquals(current[i].Parser, parser))
                    removed++;
            if (removed == 0)
                return 0;
            if (removed == current.Length) {
                ParsingCache = [];
                UnsubscribeFromParserDisposing();
                return removed;
            }
            var updated = new ParsingCacheItem[current.Length - removed];
            int destination = 0;
            for (int i = 0; i < current.Length; i++)
                if (!ReferenceEquals(current[i].Parser, parser))
                    updated[destination++] = current[i];
            ParsingCache = updated;
            return removed;
        }
    }
    private void OnParserDisposing(object? sender, ParserDisposingEventArgs args) {
        lock (ParsingCacheSharedLock) {
            var current = ParsingCache;
            bool contains = false;
            for (int i = 0; i < current.Length; i++)
                if (ReferenceEquals(current[i].Parser, args.Parser)) {
                    contains = true;
                    break;
                }
            if (!contains)
                return;
            if (args.Mode == ParserInvalidationMode.CheckUsage) {
                args.Cancel = true;
                return;
            }
            int kept = 0;
            for (int i = 0; i < current.Length; i++)
                if (!ReferenceEquals(current[i].Parser, args.Parser))
                    kept++;
            var updated = new ParsingCacheItem[kept];
            int destination = 0;
            for (int i = 0; i < current.Length; i++)
                if (!ReferenceEquals(current[i].Parser, args.Parser))
                    updated[destination++] = current[i];
            ParsingCache = updated;
            if (kept == 0)
                UnsubscribeFromParserDisposing();
        }
    }
    private void UnsubscribeFromParserDisposing() {
        if (!_subscribedToParserDisposing)
            return;
        TypeParser.ParserDisposing -= OnParserDisposing;
        _subscribedToParserDisposing = false;
    }
    private static ITypeParser[] GetDistinctParsers(ParsingCacheItem[] entries) {
        var parsers = new List<ITypeParser>(entries.Length);
        for (int i = 0; i < entries.Length; i++) {
            var parser = entries[i].Parser;
            bool found = false;
            for (int j = 0; j < parsers.Count; j++)
                if (ReferenceEquals(parsers[j], parser)) {
                    found = true;
                    break;
                }
            if (!found)
                parsers.Add(parser);
        }
        return [.. parsers];
    }
    /// <summary>
    /// Checks whether any used parameter still needs database parameter details.
    /// </summary>
    /// <returns><see langword="false"/> when every used parameter is already cached.</returns>
    public bool NeedToCache(Span<bool> usageMap)
        => Parameters.NeedToCache(usageMap);
    /// <summary>
    /// Checks whether any supplied parameter still needs database parameter details.
    /// </summary>
    /// <returns><see langword="false"/> when every used parameter is already cached.</returns>
    public bool NeedToCache(object?[] variables)
        => Parameters.NeedToCache(variables);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool[] CreateUsageMap() {
        var count = Mapper.Count;
        return count == 0 ? Array.Empty<bool>() : new bool[count];
    }
    internal bool[] CreateUsageMap(object?[] variables) {
        var usage = CreateUsageMap();
        if (Parameters.HasDefaultSet)
            Parameters.FillUsageMap(variables, usage);
        else
            for (int i = 0; i < variables.Length; i++)
                usage[i] = variables[i] is not null;
        return usage;
    }
    /// <summary>
    /// Reads parameter details from <paramref name="cmd"/> and stores them for later runs.
    /// Call this after execution when a custom command needs to teach this command its parameter details.
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
    /// Sets the database details for one parameter.
    /// Use this when the required type or size cannot be inferred from a value.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="paramName"/> names a bindable parameter.</returns>
    public bool UpdateParamCache(string paramName, DbParamInfo paramInfo) {
        var ind = Mapper.GetIndex(paramName);
        if (ind < 0 || ind >= StartBaseHandlers)
            return false;
        return Parameters.UpdateCache(ind, paramInfo);
    }
    /// <summary>
    /// Sets the database details for one parameter by its zero based index.
    /// Use this for parameters that are bound by position.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="variableIndex"/> names a bindable parameter.</returns>
    public bool UpdateParamCache(int variableIndex, DbParamInfo paramInfo) {
        if ((uint)variableIndex >= (uint)StartBaseHandlers)
            return false;
        return Parameters.UpdateCache(variableIndex, paramInfo);
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
            else if (Parameters.HasDefaultSet && varInfos[i].HasDefaultSet)
                varInfos[i].SetDefault(Unsafe.Add(ref pKeys, i), cmd);
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

        if (!Parameters.HasDefaultSet)
            SetText(cmd, QueryText.Parse(variables));
        else {
            var usage = CreateUsageMap(variables);
            SetText(cmd, QueryText.Parse(usage, variables.AsSpan(varInfos.Length, handlers.Length)));
        }

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
            else if (Parameters.HasDefaultSet && varInfos[i].HasDefaultSet)
                varInfos[i].SetDefault(Unsafe.Add(ref pKeys, i), cmd);
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

        if (!Parameters.HasDefaultSet)
            SetText(cmd, QueryText.Parse(variables));
        else {
            var usage = CreateUsageMap(variables);
            SetText(cmd, QueryText.Parse(usage, variables.AsSpan(varInfos.Length, handlers.Length)));
        }

        return true;
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand(IDbCommand cmd, object? parameterObj, Span<bool> usageMap) {
        if (parameterObj is null) {
            usageMap.Clear();
            ApplyDefaults(cmd, usageMap);
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
            ApplyDefaults(cmd, usageMap);
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
        var accessor = GetDirectAccessor(typeof(T).TypeHandle.Value, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, T parameterObj, Span<bool> usageMap) where T : notnull {
        var accessor = GetDirectAccessor(typeof(T).TypeHandle.Value, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(IDbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        var accessor = GetDirectAccessor(typeof(T).TypeHandle.Value, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SetCommand<T>(DbCommand cmd, ref T parameterObj, Span<bool> usageMap) where T : notnull {
        var accessor = GetDirectAccessor(typeof(T).TypeHandle.Value, typeof(T));
        if (!typeof(T).IsValueType)
            return FinishSetCommand(cmd, accessor.Invoke(parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
        var typed = Unsafe.As<DirectAccessor, DirectAccessor<T>>(ref accessor);
        return FinishSetCommand(cmd, typed.InvokeTyped(ref parameterObj, cmd, Parameters._variablesInfo, ref usageMap), usageMap);
    }
    private Span<object?> EmptyHandlerValues()
        => QueryText.HandlerValuesLength <= 0 ? default : new object?[QueryText.HandlerValuesLength];
    /// <summary>
    /// Gets the accessor used to bind the supplied parameter type directly to a database command.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DirectAccessor GetDirectAccessor(IntPtr handle, Type type) {
        var accessors = Volatile.Read(ref _accessors);
        for (int i = 0; i < accessors.Length; i++)
            if (accessors[i].Handle.Value == handle) {
                if (accessors[i].Accessor is DirectAccessor direct)
                    return direct;
                if (accessors[i].Accessor is AccessorPair pair)
                    return pair.Direct;
                break;
            }
        lock (TypeAccessorSharedLock) {
            accessors = _accessors;
            for (int i = 0; i < accessors.Length; i++)
                if (accessors[i].Handle.Value == handle) {
                    if (accessors[i].Accessor is DirectAccessor direct)
                        return direct;
                    if (accessors[i].Accessor is AccessorPair pair)
                        return pair.Direct;

                    var createdDirect = ParameterAccessorGenerator.CreateDirect(type, Mapper, Parameters._specialHandlers,
                        StartSpecialHandlers, StartBoolCond);
                    var updated = new (RuntimeTypeHandle Handle, object Accessor)[accessors.Length];
                    Array.Copy(accessors, updated, accessors.Length);
                    updated[i] = (type.TypeHandle, new AccessorPair(createdDirect, (UseWithAccessor)accessors[i].Accessor));
                    Volatile.Write(ref _accessors, updated);
                    return createdDirect;
                }
            var accessor = ParameterAccessorGenerator.CreateDirect(type, Mapper, Parameters._specialHandlers,
                StartSpecialHandlers, StartBoolCond);
            Volatile.Write(ref _accessors, [.. accessors, (type.TypeHandle, accessor)]);
            return accessor;
        }
    }

    /// <summary>Gets the accessor used by <c>UseWith</c> for the supplied parameter type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UseWithAccessor GetUseWithAccessor(IntPtr handle, Type type) {
        var accessors = Volatile.Read(ref _accessors);
        for (int i = 0; i < accessors.Length; i++)
            if (accessors[i].Handle.Value == handle) {
                if (accessors[i].Accessor is UseWithAccessor useWith)
                    return useWith;
                if (accessors[i].Accessor is AccessorPair pair)
                    return pair.UseWith;
                break;
            }
        lock (TypeAccessorSharedLock) {
            accessors = _accessors;
            for (int i = 0; i < accessors.Length; i++)
                if (accessors[i].Handle.Value == handle) {
                    if (accessors[i].Accessor is UseWithAccessor useWith)
                        return useWith;
                    if (accessors[i].Accessor is AccessorPair pair)
                        return pair.UseWith;

                    var createdUseWith = ParameterAccessorGenerator.CreateUseWith(type, Mapper, Parameters._specialHandlers,
                        StartSpecialHandlers, StartBoolCond);
                    var updated = new (RuntimeTypeHandle Handle, object Accessor)[accessors.Length];
                    Array.Copy(accessors, updated, accessors.Length);
                    updated[i] = (type.TypeHandle, new AccessorPair((DirectAccessor)accessors[i].Accessor, createdUseWith));
                    Volatile.Write(ref _accessors, updated);
                    return createdUseWith;
                }
            var accessor = ParameterAccessorGenerator.CreateUseWith(type, Mapper, Parameters._specialHandlers,
                StartSpecialHandlers, StartBoolCond);
            Volatile.Write(ref _accessors, [.. accessors, (type.TypeHandle, accessor)]);
            return accessor;
        }
    }

    /// <summary>Gets the parameter types and accessor kinds currently held by this command.</summary>
    public (Type ParameterType, ParameterAccessorKinds Accessors)[] GetCachedParameterAccessors() {
        var current = Volatile.Read(ref _accessors);
        var result = new (Type ParameterType, ParameterAccessorKinds Accessors)[current.Length];
        for (int i = 0; i < current.Length; i++)
            result[i] = (Type.GetTypeFromHandle(current[i].Handle)!, GetAccessorKinds(current[i].Accessor));
        return result;
    }

    /// <summary>
    /// Removes the selected accessors for <paramref name="parameterType"/>.
    /// </summary>
    /// <returns>The accessor kinds that were present and removed.</returns>
    public ParameterAccessorKinds InvalidateParameterAccessor(Type parameterType, ParameterAccessorKinds accessors) {
        ArgumentNullException.ThrowIfNull(parameterType);
        if (accessors == ParameterAccessorKinds.None || (accessors & ~ParameterAccessorKinds.Both) != 0)
            throw new ArgumentOutOfRangeException(nameof(accessors));
        IntPtr handle = parameterType.TypeHandle.Value;
        lock (TypeAccessorSharedLock) {
            var current = _accessors;
            int index = -1;
            for (int i = 0; i < current.Length; i++)
                if (current[i].Handle.Value == handle) {
                    index = i;
                    break;
                }
            if (index < 0)
                return ParameterAccessorKinds.None;
            var removed = GetAccessorKinds(current[index].Accessor) & accessors;
            if (removed == ParameterAccessorKinds.None)
                return ParameterAccessorKinds.None;
            if (current[index].Accessor is AccessorPair pair && removed != ParameterAccessorKinds.Both) {
                var retained = new (RuntimeTypeHandle Handle, object Accessor)[current.Length];
                Array.Copy(current, retained, current.Length);
                retained[index] = (current[index].Handle,
                    removed == ParameterAccessorKinds.Direct ? pair.UseWith : pair.Direct);
                Volatile.Write(ref _accessors, retained);
                return removed;
            }
            if (current.Length == 1) {
                Volatile.Write(ref _accessors, []);
                return removed;
            }
            var updated = new (RuntimeTypeHandle Handle, object Accessor)[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, updated.Length - index);
            Volatile.Write(ref _accessors, updated);
            return removed;
        }
    }

    private static ParameterAccessorKinds GetAccessorKinds(object accessor) {
        if (accessor is AccessorPair)
            return ParameterAccessorKinds.Both;
        return accessor is DirectAccessor ? ParameterAccessorKinds.Direct : ParameterAccessorKinds.UseWith;
    }

    private void ClearParameterAccessors() {
        lock (TypeAccessorSharedLock)
            Volatile.Write(ref _accessors, []);
    }

    internal object QueryRuntime(Type type, DbConnection cnn, object? parametersObj, DbTransaction? transaction, int? timeout) {
        var cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal object QueryRuntime(Type type, DbConnection cnn, out DbCommand cmd, object? parametersObj, DbTransaction? transaction, int? timeout) {
        cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), false)!;
    }
    internal Task<object> QueryRuntimeAsync(Type type, DbConnection cnn, object? parametersObj, DbTransaction? transaction, int? timeout, CancellationToken ct) {
        var cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal Task<object> QueryRuntimeAsync(Type type, DbConnection cnn, out DbCommand cmd, object? parametersObj, DbTransaction? transaction, int? timeout, CancellationToken ct) {
        cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), false, ct));
    }
    internal object QueryRuntime(Type type, IDbConnection cnn, object? parametersObj, IDbTransaction? transaction, int? timeout) {
        var cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal object QueryRuntime(Type type, IDbConnection cnn, out IDbCommand cmd, object? parametersObj, IDbTransaction? transaction, int? timeout) {
        cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), false)!;
    }
    internal Task<object> QueryRuntimeAsync(Type type, IDbConnection cnn, object? parametersObj, IDbTransaction? transaction, int? timeout, CancellationToken ct) {
        var cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal Task<object> QueryRuntimeAsync(Type type, IDbConnection cnn, out IDbCommand cmd, object? parametersObj, IDbTransaction? transaction, int? timeout, CancellationToken ct) {
        cmd = cnn.GetCommand(transaction, timeout);
        var usage = CreateUsageMap();
        SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), false, ct));
    }
    private static async Task<object> BoxRuntime(Task<object?> result) => (object)(await result.ConfigureAwait(false))!;
    internal object QueryRuntime<TObj>(Type type, DbConnection cnn, TObj parametersObj, DbTransaction? transaction, int? timeout) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync<TObj>(Type type, DbConnection cnn, TObj parametersObj, DbTransaction? transaction, int? timeout, CancellationToken ct) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime<TObj>(Type type, IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction, int? timeout) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync<TObj>(Type type, IDbConnection cnn, TObj parametersObj, IDbTransaction? transaction, int? timeout, CancellationToken ct) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime<TObj>(Type type, DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction, int? timeout) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, ref parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync<TObj>(Type type, DbConnection cnn, ref TObj parametersObj, DbTransaction? transaction, int? timeout, CancellationToken ct) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, ref parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime<TObj>(Type type, IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction, int? timeout) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, ref parametersObj, usage);
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync<TObj>(Type type, IDbConnection cnn, ref TObj parametersObj, IDbTransaction? transaction, int? timeout, CancellationToken ct) where TObj : notnull {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = CreateUsageMap(); SetCommand(cmd, ref parametersObj, usage);
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime(Type type, DbConnection cnn, object?[] variables, DbTransaction? transaction, int? timeout) {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = variables.ToBoolArray(); SetText(cmd, QueryText.Parse(variables));
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync(Type type, DbConnection cnn, object?[] variables, DbTransaction? transaction, int? timeout, CancellationToken ct) {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = variables.ToBoolArray(); SetText(cmd, QueryText.Parse(variables));
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime(Type type, IDbConnection cnn, object?[] variables, IDbTransaction? transaction, int? timeout) {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = variables.ToBoolArray(); SetText(cmd, QueryText.Parse(variables));
        return cmd.Query(type, new RuntimeParserLinker(this, type, usage), true)!;
    }
    internal Task<object> QueryRuntimeAsync(Type type, IDbConnection cnn, object?[] variables, IDbTransaction? transaction, int? timeout, CancellationToken ct) {
        var cmd = cnn.GetCommand(transaction, timeout); var usage = variables.ToBoolArray(); SetText(cmd, QueryText.Parse(variables));
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, usage), true, ct));
    }
    internal object QueryRuntime(Type type, IDbCommand cmd, object?[] variables, bool disposeCommand) {
        SetText(cmd, QueryText.Parse(variables));
        return cmd.Query(type, new RuntimeParserLinker(this, type, variables.ToBoolArray()), disposeCommand)!;
    }
    internal Task<object> QueryRuntimeAsync(Type type, IDbCommand cmd, object?[] variables, bool disposeCommand, CancellationToken ct) {
        SetText(cmd, QueryText.Parse(variables));
        return BoxRuntime(cmd.QueryAsync(type, new RuntimeParserLinker(this, type, variables.ToBoolArray()), disposeCommand, ct));
    }

    /// <summary>
    /// Releases resources used by this command.
    /// The command cannot be used after disposal.
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        InvalidateParsers();
        ClearParameterAccessors();
        Mapper.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class AccessorPair(DirectAccessor direct, UseWithAccessor useWith) {
        internal readonly DirectAccessor Direct = direct;
        internal readonly UseWithAccessor UseWith = useWith;
    }
    private bool FinishSetCommand(IDbCommand cmd, object?[] handlerValues, Span<bool> usageMap) {
        ApplyDefaults(cmd, usageMap);
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
    private void ApplyDefaults(IDbCommand cmd, Span<bool> usageMap) {
        if (!Parameters.HasDefaultSet)
            return;
        ref string pKeys = ref Mapper.KeysStartPtr;
        for (int i = 0; i < Parameters._variablesInfo.Length; i++)
            if (!usageMap[i] && Parameters._variablesInfo[i].HasDefaultSet) {
                Parameters._variablesInfo[i].SetDefault(Unsafe.Add(ref pKeys, i), cmd);
                usageMap[i] = true;
            }
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
