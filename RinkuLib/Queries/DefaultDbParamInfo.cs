using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RinkuLib.Queries;
/// <summary>
/// A provider that extracts parameter metadata from an existing <see cref="IDbCommand"/> 
/// to populate the query's parameter cache.
/// </summary>
public struct DefaultParamCache(IDbCommand cmd) : IDbParamInfoGetter {
    /// <summary>The command containing the parameters</summary>
    public IDbCommand Command = cmd;
    /// <inheritdoc/>
    public readonly IEnumerable<KeyValuePair<string, int>> EnumerateParameters() {
        var parameters = Command.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++)
            if (parameters[i] is IDbDataParameter p)
                yield return new(p.ParameterName, i);
    }
    /// <inheritdoc/>
    public readonly DbParamInfo MakeInfoAt(int i) {
        var p = Command.Parameters[i] as IDbDataParameter
            ?? throw new Exception($"there is no valid parameter at index {i}");
        return MakeInfo(p);
    }

    private static DbParamInfo MakeInfo(IDbDataParameter p) {
        var type = p.DbType;
        ref var arr = ref SizedDbParamCache.GetCacheArray(type);
        if (Unsafe.IsNullRef(ref arr))
            return TypedDbParamCache.Get(type);
        int inferredSize = p.Size switch {
            <= 100 => 100,
            <= 500 => 500,
            <= 4000 => 4000,
            _ => -1 // Maps to MAX/Unlimited
        };
        return SizedDbParamCache.GetOrAdd(ref arr, type, inferredSize);
    }
    /// <summary>
    /// Attempts to resolve a <see cref="DbParamInfo"/> for a specific parameter name 
    /// by inspecting the current command's parameter collection.
    /// </summary>
    public readonly bool TryGetInfo(string paramName, [MaybeNullWhen(false)] out DbParamInfo info) {
        var parameters = Command.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++) {
            if (parameters[i] is not IDbDataParameter p || !string.Equals(p.ParameterName, paramName))
                continue;
            info = MakeInfo(p);
            return true;
        }
        info = null;
        return false;
    }
}
/// <summary>
/// A provider that extracts parameter metadata from an existing <see cref="IDbCommand"/> 
/// to populate the query's parameter cache.
/// </summary>
public struct ForceInferedParamCache(IDbCommand cmd) : IDbParamInfoGetter {
    /// <summary>
    /// Delegate that create a getter returning forced infered cache when the cmd is of type T.
    /// </summary>
    public static bool GetInfoGetterMaker<T>(IDbCommand cmd, [MaybeNullWhen(false)] out IDbParamInfoGetter getter) where T : IDbCommand {
        if (cmd is not T) {
            getter = default;
            return false; 
        }
        getter = new ForceInferedParamCache(cmd);
        return true;
    }
    /// <summary>The command containing the parameters</summary>
    public IDbCommand Command = cmd;
    /// <inheritdoc/>
    public readonly IEnumerable<KeyValuePair<string, int>> EnumerateParameters() {
        var parameters = Command.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++)
            if (parameters[i] is IDbDataParameter p)
                yield return new(p.ParameterName, i);
    }
    /// <inheritdoc/>
    public readonly DbParamInfo MakeInfoAt(int i) {
        var p = Command.Parameters[i] as IDbDataParameter
            ?? throw new Exception($"there is no valid parameter at index {i}");
        return InferedDbParamCache.ForceInfered;
    }
    /// <summary>
    /// Attempts to resolve a <see cref="DbParamInfo"/> for a specific parameter name 
    /// by inspecting the current command's parameter collection.
    /// </summary>
    public readonly bool TryGetInfo(string paramName, [MaybeNullWhen(false)] out DbParamInfo info) {
        var parameters = Command.Parameters;
        var count = parameters.Count;
        for (int i = 0; i < count; i++) {
            if (parameters[i] is not IDbDataParameter p || !string.Equals(p.ParameterName, paramName))
                continue;
            info = InferedDbParamCache.ForceInfered;
            return true;
        }
        info = null;
        return false;
    }
}
/// <summary>
/// Represents metadata for fixed-type database parameters (e.g., Integers, Booleans) 
/// where size is not a factor in performance optimization.
/// </summary>
public class TypedDbParamCache : DbParamInfo {
    /// <summary> Returns a cached instance for the specified <see cref="DbType"/>. </summary>
    public static DbParamInfo Get(DbType type, int size = 0) {
        if (SizedDbParamCache.TryGet(type, size, out var cache))
            return cache;
        return CachedItems[(int)type];
    }
    /// <summary> Returns a cached instance for the specified <see cref="DbType"/>. </summary>
    public static TypedDbParamCache Get(DbType type) => CachedItems[(int)type];
    /// <summary>The <see cref="DbType"/> that will be used to create the parameter.</summary>
    public readonly DbType Type;
    private TypedDbParamCache(DbType type) : base(true) { 
        this.Type = type;
    }
    private static readonly TypedDbParamCache[] CachedItems;
    static TypedDbParamCache() {
        CachedItems = new TypedDbParamCache[28];
        for (int i = 0; i < 28; i++)
            CachedItems[i] = new((DbType)i);
        //should skip 24
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
            return true;
        }
        p.Value = newValue;
        return true;
    }
    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue) 
        => cmd.Parameters.Remove(currentValue);
    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
}
/// <summary>
/// Represents metadata for variable-length database parameters (e.g., Strings, Binary). 
/// It optimizes performance by rounding sizes to common thresholds (100, 500, 4000) 
/// to reduce query plan fragmentation.
/// </summary>
public class SizedDbParamCache : DbParamInfo {
    /// <summary>
    /// Retrieves or creates a sized cache entry using a binary search and 
    /// threshold-based rounding.
    /// </summary>
    public static SizedDbParamCache Get(DbType type, int size) {
        ref var arr = ref GetCacheArray(type);
        if (Unsafe.IsNullRef(ref arr))
            throw new ArgumentException($"Type {type} does not support a custom size parameter.");
        return GetOrAdd(ref arr, type, size);
    }
    /// <summary>The <see cref="DbType"/> that will be used to create the parameter.</summary>
    public readonly DbType Type;
    /// <summary>The size that will be used to create the parameter.</summary>
    public readonly int Size;
    private SizedDbParamCache(DbType type, int size) : base(true) {
        this.Type = type;
        this.Size = size;
    }
    // Dedicated caches for the most common sized types
    private static SizedDbParamCache[] _stringCache = [];
    private static SizedDbParamCache[] _ansiStringCache = [];
    private static SizedDbParamCache[] _binaryCache = [];
    private static SizedDbParamCache[] _xmlCache = [];
    private static SizedDbParamCache[] _ansiStringFixedLengthCache = [];
    private static SizedDbParamCache[] _stringFixedLengthCache = [];
    internal static ref SizedDbParamCache[] GetCacheArray(DbType type) {
        if (type == DbType.String) return ref _stringCache;
        if (type == DbType.AnsiString) return ref _ansiStringCache;
        if (type == DbType.Binary) return ref _binaryCache;
        if (type == DbType.Xml) return ref _xmlCache;
        if (type == DbType.AnsiStringFixedLength) return ref _ansiStringFixedLengthCache;
        if (type == DbType.StringFixedLength) return ref _stringFixedLengthCache;
        return ref Unsafe.NullRef<SizedDbParamCache[]>();
    }
    /// <summary>Try to retrieve the singleton instance corresponding to the parameters or creates it</summary>
    /// <returns><see langword="false"/> when the type is not a <see cref="DbType"/> that contains a size.</returns>
    public static bool TryGet(DbType type, int size, [MaybeNullWhen(false)] out SizedDbParamCache cache) {
        ref var arr = ref GetCacheArray(type);
        cache = null;
        if (Unsafe.IsNullRef(ref arr))
            return false;
        cache = GetOrAdd(ref arr, type, size);
        return true;
    }
    internal static SizedDbParamCache GetOrAdd(ref SizedDbParamCache[] cache, DbType type, int size) {
        int low = 0;
        int high = cache.Length - 1;
        if (high > 512)
            return new SizedDbParamCache(type, size);
        while (low <= high) {
            int mid = low + ((high - low) >> 1);
            int midSize = cache[mid].Size;

            if (midSize == size)
                return cache[mid];
            if (midSize < size)
                low = mid + 1;
            else
                high = mid - 1;
        }

        var newItem = new SizedDbParamCache(type, size);
        Array.Resize(ref cache, cache.Length + 1);
        if (low < cache.Length - 1)
            Array.Copy(cache, low, cache, low + 1, cache.Length - low - 1);

        cache[low] = newItem;
        return newItem;
    }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
            return true;
        }
        p.Value = newValue;
        return true;
    }
    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object? currentValue) 
        => cmd.Parameters.Remove(currentValue);
    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Size = Size;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Size = Size;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.DbType = Type;
        p.Value = value;
        p.Size = Size;
        cmd.Parameters.Add(p);
        return true;
    }
}
/// <summary>
/// A fallback implementation used when specific database types have not yet been 
/// resolved or cached. Relies on driver-level type inference.
/// </summary>
public class InferedDbParamCache : DbParamInfo {
    /// <summary>Singleton instance of the infered cache</summary>
    public static readonly InferedDbParamCache Instance = new(false);
    /// <summary>Singleton instance of the infered cache</summary>
    public static readonly InferedDbParamCache ForceInfered = new(true);
    private InferedDbParamCache(bool isCached) : base(isCached) { }
    /// <inheritdoc/>
    public override bool Update(IDbCommand cmd, ref object? currentValue, object? newValue) {
        if (currentValue is not IDbDataParameter p)
            return false;
        if (newValue is null) {
            cmd.Parameters.Remove(currentValue);
            currentValue = null;
            return true;
        }
        p.Value = newValue;
        return true;
    }
    /// <inheritdoc/>
    public override void Remove(IDbCommand cmd, object currentValue) 
        => cmd.Parameters.Remove(currentValue);
    /// <inheritdoc/>
    public override bool Use(string paramName, IDbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
    /// <inheritdoc/>
    public override bool SaveUse(string paramName, IDbCommand cmd, ref object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.Value = value;
        cmd.Parameters.Add(p);
        value = p;
        return true;
    }
    /// <inheritdoc/>
    public override bool Use(string paramName, DbCommand cmd, object value) {
        var p = cmd.CreateParameter();
        p.ParameterName = paramName;
        p.Value = value;
        cmd.Parameters.Add(p);
        return true;
    }
}