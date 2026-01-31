using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// Defines the contract for an executable query unit, managing the transition 
/// from a state-snapshot to a configured database command.
/// </summary>
public interface IQueryCommand : IParserCache {
#pragma warning disable CA2211
    public static char DefaultVariableChar = '@';
#pragma warning restore CA2211
    /// <summary>
    /// Configures the <paramref name="cmd"/> using a full state-snapshot of the query.
    /// </summary>
    /// <param name="cmd">The command to be populated.</param>
    /// <param name="variables">
    /// A unified array representing the state of all query elements (Selects, Conditions, 
    /// Variables, and Handlers). The array layout must strictly adhere to the <see cref="Mapper"/>.
    /// </param>
    /// <returns>True if the command was successfully prepared.</returns>
    public bool SetCommand(IDbCommand cmd, object?[] variables);
    /// <summary>
    /// Performance-optimized overload for concrete <see cref="DbCommand"/> types.
    /// </summary>
    public bool SetCommand(DbCommand cmd, object?[] variables);
    /// <summary> The shared coordinate system used to map names to array indices. </summary>
    public Mapper Mapper { get; }
    /// <summary> The index where literal-injection handlers begin. </summary>
    public int StartBaseHandlers { get; }
    /// <summary> The index where complex parameter handlers begin. </summary>
    public int StartSpecialHandlers { get; }
    /// <summary> The index where standard database parameters begin. </summary>
    public int StartVariables { get; }
    /// <summary> The index marking the end of the selectable column definitions (and the total count of select segments). </summary>
    public int EndSelect { get; }
    public bool NeedToCache(object?[] variables);
}
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
public class QueryCommand : IQueryCommand {
    public static QueryCommand New(string query, bool extractSelects = false, char variableChar = default) {
        var factory = new QueryFactory(query, extractSelects, variableChar == default ? IQueryCommand.DefaultVariableChar : variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap);
        return new QueryCommand(factory);
    }
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
    public readonly ParsingCache? ParsingCache;
    public readonly int StartBaseHandlers;
    public readonly int StartSpecialHandlers;
    public readonly int StartVariables;
    public readonly int EndSelect;
    public QueryCommand(string query, bool extractSelects = false, char variableChar = default)
        : this(new QueryFactory(query, extractSelects, variableChar == default ? IQueryCommand.DefaultVariableChar : variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap)) { }
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
        Parameters = new(StartVariables, StartSpecialHandlers, specialHandlers);
        if (factory.HasSelect)
            ParsingCache = ParsingCache.New(factory.NbSelects);
    }
    public IParserCache? GetCacheAndParser<T>(object?[] variables, out CommandBehavior behavior, out Func<DbDataReader, T>? parser) {
        IParserCache? cache = null;
        parser = null;
        behavior = default;
        ParsingCache?.GetCacheAndParser(variables, out parser, out behavior, out cache);
        if (NeedToCache(variables))
            cache = cache is null ? this : new CacheWrapper(cache, this);
        return cache;
    }
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
    public void UpdateCache<T>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, T>? parsingFunc)
        => UpdateCache(cmd);
    private bool UpdateCache<T>(T infoGetter) where T : IDbParamInfoGetter {
        foreach (var item in infoGetter.EnumerateParameters()) {
            var ind = Mapper.GetIndex(item.Key) - StartVariables;
            if (Parameters.IsCached(ind))
                continue;
            Parameters.UpdateCache(ind, infoGetter.MakeInfoAt(item.Value));
        }
        Parameters.UpdateSpecialHandlers(infoGetter);
        Parameters.UpdateNbCached();
        return true;
    }


    public void UpdateParser<T>(Func<DbDataReader, T> _, CommandBehavior __) { }
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
                handlers[i].Use(cmd, ref currentVar);
        }

        cmd.CommandText = QueryText.Parse(variables);

        return true;
    }
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
                handlers[i].Use(cmd, ref currentVar);
        }

        cmd.CommandText = QueryText.Parse(variables);

        return true;
    }
}
