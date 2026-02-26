namespace RinkuLib.Queries;

/// <summary>
/// A registry that manages the metadata and caching state for all variables and special handlers 
/// associated with a query.
/// </summary>
/// <remarks>
/// This class serves as the central state provider for the query's parameter lifecycle. It tracks 
/// which parameters have resolved their database-specific metadata and provides an efficient 
/// mechanism to determine if the query requires further metadata resolution based on the 
/// current execution's variables.
/// </remarks>
public sealed class QueryParameters : IDbParamCache {
    internal DbParamInfo[] _variablesInfo;
    /// <summary>Direct acces to the cache of the parameters</summary>
    public ReadOnlySpan<DbParamInfo> VariablesInfo => _variablesInfo;
    internal SpecialHandler[] _specialHandlers;
    /// <summary>Direct acces to the cache of the special handlers</summary>
    public ReadOnlySpan<SpecialHandler> SpecialHandlers => _specialHandlers;
    internal int NbNonCached;
    internal int[] _nonCachedIndexes;
    /// <summary>Create a new instance of <see cref="QueryParameters"/></summary>
    public QueryParameters(int NbNormalVariables, SpecialHandler[] specialHandlers) {
        _variablesInfo = new DbParamInfo[NbNormalVariables];
        for (int i = 0; i < NbNormalVariables; i++)
            _variablesInfo[i] = InferedDbParamCache.Instance;
        _specialHandlers = specialHandlers;
        var total = NbNormalVariables + specialHandlers.Length;
        _nonCachedIndexes = new int[total];
        NbNonCached = total;
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
        if (isDifferentCached) {
            var oldArray = _nonCachedIndexes;
            int len = oldArray.Length;
            var nbNon = new int[len + 1];
            int i = 0;
            while (i < len && oldArray[i] < ind) {
                nbNon[i] = oldArray[i];
                i++;
            }
            nbNon[i] = ind;
            while (i < len) {
                nbNon[i + 1] = oldArray[i];
                i++;
            }
            Interlocked.Exchange(ref _nonCachedIndexes, nbNon);
            Interlocked.Exchange(ref NbNonCached, nbNon.Length);
        }
        return true;
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
        _nonCachedIndexes = nonCachedIndexes[..total].ToArray();
        NbNonCached = total;
    }
    /// <summary>
    /// Evaluates the provided variables to determine if any non-cached parameter 
    /// currently possesses data that requires metadata resolution.
    /// </summary>
    public bool NeedToCache(object?[] variables) {
        if (NbNonCached == 0)
            return false;
        for (int i = 0; i < _nonCachedIndexes.Length; i++)
            if (variables[_nonCachedIndexes[i]] is not null)
                return true;
        return false;
    }
    /// <summary>
    /// Evaluates the provided variables to determine if any non-cached parameter 
    /// currently possesses data that requires metadata resolution.
    /// </summary>
    public bool NeedToCache(Span<bool> usageMap) {
        if (NbNonCached == 0)
            return false;
        for (int i = 0; i < _nonCachedIndexes.Length; i++)
            if (usageMap[_nonCachedIndexes[i]])
                return true;
        return false;
    }
}
