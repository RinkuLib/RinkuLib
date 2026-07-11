using System.Buffers;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Reads a SQL template once and produces the pieces a <see cref="QueryCommand"/> runs from, the stripped
/// text, the parts that can switch on and off, and the map from key names to slots. You meet it when
/// subclassing <see cref="QueryCommand"/> to change how templates are read, most code never touches it.
/// </summary>
public struct QueryFactory {
    /// <summary>
    /// The built-in handler letters and how each expands, keyed by suffix. <c>S</c> quotes a string, <c>R</c>
    /// injects raw text, <c>N</c> writes a number. App-wide and mutable, add a letter here to register your
    /// own handler for every command.
    /// </summary>
    public static readonly LetterMap<HandlerGetter<IQuerySegmentHandler>> BaseHandlerMapper = new(
        ('S', StringVariableHandler.Build),
        ('R', RawVariableHandler.Build),
        ('N', NumberVariableHandler.Build)
    );
#pragma warning disable CA2211
    /// <summary>The character that marks a variable when a command does not name its own. <c>@</c> to start.</summary>
    public static char DefaultVariableChar = '@';
#pragma warning restore CA2211
    /// <summary>The template text with the markers stripped, the SQL a run with everything present would send.</summary>
    public string Query;
    /// <summary>The template broken into the runs of text and handler spots that make up the SQL.</summary>
    public QuerySegment[] Segments;
    /// <summary>The optional parts and the keys that switch them on or off.</summary>
    public Condition[] Conditions;
    /// <summary>The map from each key name to its slot, the shared numbering the rest of the pieces address.</summary>
    public Mapper Mapper;
    /// <summary>How many plain value-carrying variables the template has, required and optional together.</summary>
    public int NbNormalVar;
    /// <summary>How many variables are expanded by a special handler.</summary>
    public int NbSpecialHandlers;
    /// <summary>How many variables are expanded by a built-in base handler.</summary>
    public int NbBaseHandlers;
    /// <summary>How many keys are required, so a run must always supply them.</summary>
    public int NbRequired;
    /// <summary>How many conditional markers are neither a variable nor a projected column.</summary>
    public int NbNonVarComment;

    /// <summary>Which base-handler letters are free to use, the ones a special handler has not claimed.</summary>
    public uint BaseHandlerPresenceMap;
    /// <summary>Whether a suffix letter is a base handler that is free to use here.</summary>
    public readonly bool IsBaseHandler(char c) {
        int i = (c | 0x20) - 'a';
        return (uint)i < 26 && (BaseHandlerPresenceMap & (1U << i)) != 0;
    }
    /// <summary>
    /// Reads a template and fills in the pieces. A suffix letter that <paramref name="specialHandlerPresenceMap"/>
    /// claims is left for a special handler to bind later, one in <see cref="BaseHandlerMapper"/> binds its
    /// built-in handler, and a letter that is neither is rejected.
    /// </summary>
    /// <param name="query">The SQL template to read.</param>
    /// <param name="variableChar">The character that marks a variable, <c>@</c> when left unset.</param>
    /// <param name="specialHandlerPresenceMap">The suffix letters a special handler layer claims for itself.</param>
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

    private readonly Condition MakeSentinel() => new(Mapper.Count, Segments.Length, -1, 0, true);
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
        return new(condMapperInd, segInd, end - segInd, isOrIdentifier, !cond.Flags.HasFlag(CondFlags.IsNot));
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
