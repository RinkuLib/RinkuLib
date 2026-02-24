using System.Buffers;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// A structural template provider that compiles raw SQL into data-only blueprints.
/// It defines the geometry of query fragments and establishes the memory contract for data binding.
/// </summary>
/// <remarks>
/// <para><b>Template Role:</b></para>
/// <para>The <see cref="QueryFactory"/> is a structural provider. It generates <see cref="QuerySegment"/> 
/// and <see cref="Condition"/> arrays that serve as instruction sets for an assembly engine. 
/// These are data-only structures; they provide the offsets, indices, and jump-ahead counts 
/// required to build a final query string without performing the assembly logic themselves.</para>
/// 
/// <para><b>The Mapper Contract:</b></para>
/// <para>The <see cref="Mapper"/> is the central registry. It enforces a specific tier-ordered index 
/// for every key (Selects → Comments → Vars → Special → Base). External tools use this Mapper 
/// to store values or states at specific integer indices. The <see cref="Condition"/> structs 
/// then use these pre-assigned indices to look up that data in O(1) time during assembly.</para>
/// 
/// <para><b>Assembly Model:</b></para>
/// <para>The design facilitates a co-linear iteration where <see cref="Segments"/> are processed 
/// sequentially, gated by <see cref="Conditions"/>, until the <b>Sentinel Condition</b> signals 
/// completion. However, since the output is raw data, alternative engine implementations are 
/// free to interpret these blueprints in any way that respects the index contract.</para>
/// </remarks>
public struct QueryFactory {
    /// <summary>
    /// The global, mutable registry of base handlers. 
    /// Users can modify this map to add or change built-in type behaviors globally.
    /// </summary>
    public static readonly LetterMap<HandlerGetter<IQuerySegmentHandler>> BaseHandlerMapper = new([
        ('S', StringVariableHandler.Build),
        ('R', RawVariableHandler.Build),
        ('N', NumberVariableHandler.Build)
    ]);
#pragma warning disable CA2211
    /// <summary>Identifier to indicate a SQL variable</summary>
    public static char DefaultVariableChar = '@';
#pragma warning restore CA2211
    /// <summary>The normalized SQL query string with all markers and metadata stripped.</summary>
    public string Query;
    /// <summary>
    /// A contiguous sequence of <see cref="QuerySegment"/> objects. 
    /// Together, they form the complete, normalized SQL string.
    /// </summary>
    public QuerySegment[] Segments;
    /// <summary>
    /// A table of pre-calculated jump data. Each entry defines a logical footprint on the 
    /// <see cref="Segments"/> array and the skip-count for the logic tree.
    /// </summary>
    /// <remarks>
    /// The final entry is a <b>Sentinel Condition</b>, used to define the logical end of the query 
    /// and prevent out-of-bounds iteration during assembly.
    /// </remarks>
    public Condition[] Conditions;
    /// <summary>
    /// The authoritative index registry. Defines the integer offsets used by <see cref="Condition.CondIndex"/> 
    /// to retrieve external state/values. This class is fully functional and used to bridge 
    /// string keys to the factory's internal integer indices.
    /// </summary>
    public Mapper Mapper;
    /// <summary>
    /// The amount of distinct not handled variables (required and optional)
    /// </summary>
    public int NbNormalVar;
    /// <summary>
    /// The amount of distinct variables that are handled by a special handler
    /// </summary>
    public int NbSpecialHandlers;
    /// <summary>
    /// The amount of distinct variables that are handled by a base handler
    /// </summary>
    public int NbBaseHandlers;
    /// <summary>
    /// The amount of distinct variables that are required (normal or handled)
    /// </summary>
    public int NbRequired;
    /// <summary>
    /// The amount of distinct conditions that are both not from dynamic projections and not a variable
    /// </summary>
    public int NbNonVarComment;

    /// <summary>
    /// Bitmask of letters ('a'-'z') representing the <b>non-claimed base handlers</b>.
    /// Derived at construction by filtering the current state of the <see cref="BaseHandlerMapper"/> letter usage
    /// against the claimed special handlers.
    /// </summary>
    public uint BaseHandlerPresenceMap;
    /// <summary>Determines if a type-suffix character is an available (non-claimed) base handler.</summary>
    public readonly bool IsBaseHandler(char c) {
        int i = (c | 0x20) - 'a';
        return (uint)i < 26 && (BaseHandlerPresenceMap & (1U << i)) != 0;
    }
    /// <summary>
    /// Initializes the template blueprints and establishes the <see cref="Mapper"/> index contract.
    /// </summary>
    /// <param name="query">The raw SQL input to be normalized and segmented.</param>
    /// <param name="variableChar">The prefix for variables (e.g., '@', ':').</param>
    /// <param name="specialHandlerPresenceMap">
    /// A bitmask of letters ('A'-'Z') claimed by external specialized logic. 
    /// </param>
    /// <remarks>
    /// <b>Handler Resolution Logic:</b>
    /// <list type="bullet">
    /// <item><b>Claimed Letters:</b> If a variable type is in <paramref name="specialHandlerPresenceMap"/>, 
    /// the segment handler is set to <see cref="IQuerySegmentHandler.NotSet"/> for later binding.</item>
    /// <item><b>Base Handlers:</b> If not claimed, the system checks <see cref="BaseHandlerMapper"/>. 
    /// If the letter exists there, it binds the associated default handler.</item>
    /// <item><b>Unsupported:</b> If a type letter is neither claimed nor found in the base registry, 
    /// the constructor will fail.</item>
    /// <item><b>Standard/Not handled Segments:</b> Segments without handler logic have their handler set to <c>null</c>.</item>
    /// </list>
    /// </remarks>
    public QueryFactory(string query, char variableChar = default, uint specialHandlerPresenceMap = 0) {
        if (variableChar == default)
            variableChar = DefaultVariableChar;
        this.BaseHandlerPresenceMap = BaseHandlerMapper.PresenceMap & ~specialHandlerPresenceMap;
        using var condInfos = QueryExtracter.Segment(query, variableChar, out Query);
        if (condInfos.Length == 0) {
            Segments = [new(0, Query.Length, 0, false, null)];
            Mapper = Mapper.GetEmptyMapper();
            Conditions = [MakeSentinel()];
            return;
        }
        Segments = MakeSegments(condInfos, variableChar);
        Mapper = MakeMapper(condInfos, variableChar);
        Conditions = MakeConditions(condInfos);
        UpdateCondToSkip();
        UpdateExecesses();
    }

    private readonly void UpdateExecesses() {
        for (int i = 0; i < Segments.Length; i++) {
            var seg = Segments[i];
            if (seg.Handler is not null || seg.ExcessOrInd == 0)
                continue;
            var endIndex = seg.Start + seg.Length - seg.ExcessOrInd;
            if (endIndex <= seg.Start)
                continue;
            if (Query[endIndex] == ';')
                Segments[i].ExcessOrInd = 0;
            else if (char.IsWhiteSpace(Query[endIndex - 1]))
                Segments[i].ExcessOrInd++;
        }
    }

    private readonly Condition MakeSentinel() => new(Mapper.Count, Segments.Length, -1, 0);
    private readonly void UpdateCondToSkip() {
        Array.Sort(Conditions);
        var len = Conditions.Length - 1;
        for (int i = 0; i < len; i++) {
            ref var cond = ref Conditions[i];
            var j = i + 1;
            if (cond.NbConditionSkip < 0) {
                var condLen = cond.Length;
                var ind = cond.SegmentInd;
                ref var endCond = ref Conditions[j];
                while (endCond.SegmentInd == ind
                    && endCond.Length == condLen
                    && endCond.NbConditionSkip < 0) {
                    j++;
                    endCond = ref Conditions[j];
                }
                cond.Length = 0;
                if (Conditions[i - 1].Length > 0)
                    Conditions[i - 1].NbConditionSkip = -Conditions[i - 1].NbConditionSkip;
            }
            else {
                var end = cond.SegmentInd + cond.Length;
                while (Conditions[j].SegmentInd < end)
                    j++;
            }
            cond.NbConditionSkip = j - i;
        }
    }

    private readonly Condition[] MakeConditions(PooledArray<CondInfo>.Locked condInfos) {
        var condLen = condInfos.Length;
        var condInd = 0;
        var conditions = new Condition[condLen - NbRequired + 1];
        for (var i = 0; i < condLen; i++) {
            ref var cond = ref condInfos[i];
            SetHandler(ref cond);
            if (cond.IsRequired)
                continue;
            conditions[condInd++] = GetOptionalCond(ref cond);
        }
        conditions[condInd] = MakeSentinel();
        return conditions;
    }

    private readonly Condition GetOptionalCond(ref CondInfo cond) {
        var segInd = 0;
        while (Segments[segInd].Start != cond.StartIndex)
            segInd++;
        var end = segInd;
        while (end < Segments.Length && Segments[end].Start != cond.EndIndex)
            end++;
        if (end < Segments.Length && cond.NextSegmentIsSection)
            Segments[end].IsSection = cond.NextSegmentIsSection;
        if (segInd - 1 >= 0 && Segments[segInd - 1].Handler is null)
            Segments[segInd - 1].ExcessOrInd = cond.PrevSegmentExcess;
        if (!Mapper.TryGetValue(cond.Cond, out var condMapperInd))
            throw new Exception($"Comment conditions using variables must exist in the query: {cond.Cond}");
        var isOrIdentifier = 0;
        if (cond.Type == CondInfo.OrComment)
            isOrIdentifier = -1;
        return new(condMapperInd, segInd, end - segInd, isOrIdentifier);
    }

    private readonly bool SetHandler(ref CondInfo cond) {
        var segInd = 0;
        if (cond.Type < CondInfo.Special)
            return false;
        while (Segments[segInd].Start != cond.VarIndex)
            segInd++;
        if (BaseHandlerMapper.TryGetValue(cond.Type, out var getter))
            Segments[segInd].Handler = getter(Mapper.GetSameKey(cond.Cond));
        else
            Segments[segInd].Handler =
#if NET8_0_OR_GREATER
                IQuerySegmentHandler
#else
                QuerySegmentHandler
#endif
                .NotSet;
        Segments[segInd].ExcessOrInd = Mapper[cond.Cond];
        return true;
    }

    private Mapper MakeMapper(PooledArray<CondInfo>.Locked condInfos, char variableChar) {
        var normalVariableInd = 0;
        var specialHandlersInd = normalVariableInd + NbNormalVar;
        var baseHandlerInd = specialHandlersInd + NbSpecialHandlers;
        var commentInd = baseHandlerInd + NbBaseHandlers;
        var nbKeys = commentInd + NbNonVarComment;
        var keys = ArrayPool<string>.Shared.Rent(nbKeys);
        for (int i = 0; i < condInfos.Length; i++) {
            var cond = condInfos[i];
            if (cond.Type >= CondInfo.Special) {
                if (IsBaseHandler(cond.Type))
                    keys[baseHandlerInd++] = cond.Cond;
                else
                    keys[specialHandlersInd++] = cond.Cond;
            }
            else if (cond.Type == CondInfo.None)
                keys[normalVariableInd++] = cond.Cond;
            else if (CondInfo.IsComment(cond.Type))
                if (cond.Cond[0] != variableChar)
                    keys[commentInd++] = cond.Cond;
        }
        var mapper = Mapper.GetMapper(keys.AsSpan(0, nbKeys));
        var count = mapper.Count;
        var startNotVar = baseHandlerInd >= nbKeys ? count : mapper.GetIndex(keys[baseHandlerInd]);
        var startBase = specialHandlersInd >= nbKeys ? count : mapper.GetIndex(keys[specialHandlersInd]);
        var startSpecial = normalVariableInd >= nbKeys ? count : mapper.GetIndex(keys[normalVariableInd]);
        NbNonVarComment = count - startNotVar;
        NbBaseHandlers = startNotVar - startBase;
        NbSpecialHandlers = startBase - startSpecial;
        NbNormalVar = startSpecial;
        ArrayPool<string>.Shared.Return(keys);
        return mapper;
    }
    private QuerySegment[] MakeSegments(PooledArray<CondInfo>.Locked condInfos, char variableChar) {
        NbSpecialHandlers = 0;
        NbBaseHandlers = 0;
        NbNormalVar = 0;
        NbRequired = 0;
        NbNonVarComment = 0;
        var segmentIndexes = new PooledArray<int>();
        segmentIndexes.Add(0);
        for (int i = 0; i < condInfos.Length; i++) {
            ref var cond = ref condInfos[i];
            if (!cond.IsFinished)
                throw new Exception($"conditions {cond.Cond} was not finished [{cond.StartIndex}-{cond.EndIndex}]");
            if (cond.Type >= CondInfo.Special) {
                segmentIndexes.Add(cond.VarIndex);
                segmentIndexes.Add(cond.VarIndex + cond.Cond.Length + 2);
                if (IsBaseHandler(cond.Type))
                    NbBaseHandlers++;
                else
                    NbSpecialHandlers++;
            }
            else if (cond.Type == CondInfo.None)
                NbNormalVar++;
            else if (CondInfo.IsComment(cond.Type))
                if (cond.Cond[0] != variableChar)
                    NbNonVarComment++;
            if (cond.IsRequired) {
                NbRequired++;
                continue;
            }
            segmentIndexes.Add(cond.StartIndex);
            segmentIndexes.Add(cond.EndIndex);
        }
        segmentIndexes.Add(Query.Length);
        var segments = ArrayPool<QuerySegment>.Shared.Rent(segmentIndexes.Length);
        var segInd = 0;
        Array.Sort(segmentIndexes.RawArray, 0, segmentIndexes.Length);
        var prevStart = 0;
        for (var i = 0; i < segmentIndexes.Length; i++) {
            var ind = segmentIndexes[i];
            if (ind == prevStart)
                continue;
            segments[segInd++] = new(prevStart, ind - prevStart, 0, false, null);
            prevStart = ind;
        }
        var res = new QuerySegment[segInd];
        Array.Copy(segments, 0, res, 0, segInd);
        ArrayPool<QuerySegment>.Shared.Return(segments);
        segmentIndexes.Dispose();
        return res;
    }
}
