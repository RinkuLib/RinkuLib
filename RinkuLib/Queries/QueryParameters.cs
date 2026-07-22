namespace RinkuLib.Queries;

/// <summary>
/// How a command binds each of its parameters, and how much it has learned so far. It holds the per-parameter
/// binding strategy and answers, for a given run, whether any parameter still needs its provider metadata
/// learned.
/// </summary>
public sealed class QueryParameters : IDbParamCache {
    internal DbParamInfo[] _variablesInfo;
    /// <summary>The binding strategy learned for each plain parameter.</summary>
    public ReadOnlySpan<DbParamInfo> VariablesInfo => _variablesInfo;
    internal SpecialHandler[] _specialHandlers;
    /// <summary>The special handlers, the ones that expand into several parameters.</summary>
    public ReadOnlySpan<SpecialHandler> SpecialHandlers => _specialHandlers;
    /// <summary>
    /// The parameters still to learn, ascending, empty once they have all settled. It is the one field the
    /// answer comes from, so a reader takes it in a single read and its length is the count. Holding a
    /// count beside it would be two fields that cannot be published together, and a reader catching them
    /// apart would answer from a count that did not match the list it walked.
    /// </summary>
    internal int[] _nonCachedIndexes;
    /// <summary>How many parameters are still to learn.</summary>
    internal int NbNonCached => _nonCachedIndexes.Length;
    /// <summary>Starts with every parameter unsettled, its binding inferred until a run teaches it more.</summary>
    public QueryParameters(int NbNormalVariables, SpecialHandler[] specialHandlers) {
        _variablesInfo = new DbParamInfo[NbNormalVariables];
        for (int i = 0; i < NbNormalVariables; i++)
            _variablesInfo[i] = InferedDbParamCache.Instance;
        _specialHandlers = specialHandlers;
        var total = NbNormalVariables + specialHandlers.Length;
        _nonCachedIndexes = new int[total];
        for (int i = 0; i < total; i++)
            _nonCachedIndexes[i] = i;
    }
    /// <inheritdoc/>
    public bool IsCached(int ind) => ind >= _variablesInfo.Length
            ? _specialHandlers[ind - _variablesInfo.Length].IsCached
            : _variablesInfo[ind].IsCached;
    /// <inheritdoc/>
    public bool UpdateCache(int ind, DbParamInfo info) {
        if (ind < 0 || ind >= _variablesInfo.Length)
            return false;
        ref var oldVal = ref _variablesInfo[ind];
        var isDifferentCached = oldVal.IsCached != info.IsCached;
        oldVal = info;
        if (!isDifferentCached)
            return true;
        var pending = _nonCachedIndexes;
        Interlocked.Exchange(ref _nonCachedIndexes, info.IsCached ? WithoutIndex(pending, ind) : WithIndex(pending, ind));
        return true;
    }
    /// <summary>
    /// The list without <paramref name="ind"/>, for a parameter that just became cached and has nothing
    /// left to learn. A list that never held it is handed back as it is, with nothing allocated.
    /// </summary>
    private static int[] WithoutIndex(int[] oldArray, int ind) {
        var at = Array.IndexOf(oldArray, ind);
        if (at < 0)
            return oldArray;
        if (oldArray.Length == 1)
            return [];
        var res = new int[oldArray.Length - 1];
        Array.Copy(oldArray, 0, res, 0, at);
        Array.Copy(oldArray, at + 1, res, at, res.Length - at);
        return res;
    }
    /// <summary>
    /// The list with <paramref name="ind"/> in its place, for a parameter that went back to being unsettled.
    /// </summary>
    private static int[] WithIndex(int[] oldArray, int ind) {
        int len = oldArray.Length;
        var res = new int[len + 1];
        int i = 0;
        while (i < len && oldArray[i] < ind) {
            res[i] = oldArray[i];
            i++;
        }
        res[i] = ind;
        while (i < len) {
            res[i + 1] = oldArray[i];
            i++;
        }
        return res;
    }
    /// <inheritdoc/>
    public bool UpdateSpecialHandlers<T>(T infoGetter) where T : IDbParamInfoGetter {
        for (int i = 0; i < _specialHandlers.Length; i++) {
            var h = _specialHandlers[i];
            if (h.IsCached)
                continue;
            h.UpdateCache(infoGetter);
        }
        return true;
    }
    /// <inheritdoc/>
    public void UpdateCachedIndexes() {
        var total = _variablesInfo.Length + _specialHandlers.Length;
        Span<int> nonCachedIndexes = total > 256 ? new int[total] : stackalloc int[total];
        total = 0;
        for (int i = 0; i < _variablesInfo.Length; i++)
            if (!_variablesInfo[i].IsCached)
                nonCachedIndexes[total++] = i;
        for (int i = 0; i < _specialHandlers.Length; i++)
            if (!_specialHandlers[i].IsCached)
                nonCachedIndexes[total++] = i + _variablesInfo.Length;
        Interlocked.Exchange(ref _nonCachedIndexes, nonCachedIndexes[..total].ToArray());
    }
    /// <summary>
    /// Whether this run uses a parameter whose provider metadata is not settled yet, so the command still has
    /// something to learn on this pass.
    /// </summary>
    /// <remarks>
    /// Every run asks this, and a command that has settled answers from the one read that finds the list
    /// empty.
    /// </remarks>
    public bool NeedToCache(object?[] variables) {
        var pending = _nonCachedIndexes;
        for (int i = 0; i < pending.Length; i++)
            if (variables[pending[i]] is not null)
                return true;
        return false;
    }
    /// <inheritdoc cref="NeedToCache(object[])"/>
    public bool NeedToCache(Span<bool> usageMap) {
        var pending = _nonCachedIndexes;
        for (int i = 0; i < pending.Length; i++)
            if (usageMap[pending[i]])
                return true;
        return false;
    }
}
