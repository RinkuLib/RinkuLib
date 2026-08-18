namespace Rinku.Querying;

/// <summary>
/// How a command binds each of its parameters, and how much it has learned so far. It holds the per-parameter
/// binding strategy and answers, for a given run, whether any parameter still needs its provider metadata
/// learned.
/// </summary>
public sealed class QueryParameters : IDbParamCache {
    internal DbParamInfo[] _variablesInfo;
    private int _nbDefaultSet;
    internal bool HasDefaultSet => Volatile.Read(ref _nbDefaultSet) != 0;
    /// <summary>The binding strategy learned for each plain parameter.</summary>
    public ReadOnlySpan<DbParamInfo> VariablesInfo => _variablesInfo;
    internal SpecialHandler[] _specialHandlers;
    /// <summary>The special handlers, the ones that expand into several parameters.</summary>
    public ReadOnlySpan<SpecialHandler> SpecialHandlers => _specialHandlers;
    internal int[] _nonCachedIndexes;
    internal int NbNonCached => _nonCachedIndexes.Length;
    /// <summary>Starts with every parameter unsettled, its binding inferred until a run teaches it more.</summary>
    public QueryParameters(int NbNormalVariables, SpecialHandler[] specialHandlers) {
        _variablesInfo = new DbParamInfo[NbNormalVariables];
        for (int i = 0; i < NbNormalVariables; i++)
            _variablesInfo[i] = DbParameterDefaults.Current.Inferred;
        for (int i = 0; i < _variablesInfo.Length; i++)
            if (_variablesInfo[i].HasDefaultSet)
                _nbDefaultSet++;
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
        if (oldVal.HasDefaultSet != info.HasDefaultSet)
            _nbDefaultSet += info.HasDefaultSet ? 1 : -1;
        var isDifferentCached = oldVal.IsCached != info.IsCached;
        oldVal = info;
        if (!isDifferentCached)
            return true;
        var pending = _nonCachedIndexes;
        Interlocked.Exchange(ref _nonCachedIndexes, info.IsCached ? WithoutIndex(pending, ind) : WithIndex(pending, ind));
        return true;
    }
    /// <summary>
    /// Returns every learned or manually pinned parameter strategy to the current inferred default. The next
    /// run relearns provider metadata for each parameter it uses.
    /// </summary>
    public void Reset() {
        var inferred = DbParameterDefaults.Current.Inferred;
        var variables = new DbParamInfo[_variablesInfo.Length];
        Array.Fill(variables, inferred);
        Interlocked.Exchange(ref _variablesInfo, variables);
        _nbDefaultSet = 0;
        for (int i = 0; i < variables.Length; i++)
            if (variables[i].HasDefaultSet)
                _nbDefaultSet++;
        for (int i = 0; i < _specialHandlers.Length; i++)
            _specialHandlers[i].ResetCache(inferred);
        UpdateCachedIndexes();
    }
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
        for (int i = 0; i < pending.Length; i++) {
            int ind = pending[i];
            if (variables[ind] is not null || (ind < _variablesInfo.Length && _variablesInfo[ind].HasDefaultSet))
                return true;
        }
        return false;
    }
    internal void FillUsageMap(object?[] variables, Span<bool> usageMap) {
        for (int i = 0; i < variables.Length; i++)
            usageMap[i] = variables[i] is not null || (i < _variablesInfo.Length && _variablesInfo[i].HasDefaultSet);
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
