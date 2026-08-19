using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Rinku.Querying.Defaults;
/// <summary>
/// Reads parameter metadata off a live <see cref="IDbCommand"/> as-is, the fallback the command uses to learn
/// its bindings when no provider-specific reader is registered.
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
            ?? throw new RinkuBindingException(ErrorCodes.InvalidParameterAtIndex, $"there is no valid parameter at index {i}");
        return DbParameterDefaults.Current.MakeInfo(p);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This reads what a provider settled on for a value it was handed, which is a guess at a shape rather
    /// than a statement of one, so the size is widened to the bucket above it and one plan serves every
    /// value of a similar length. <see cref="MakeDeclaredInfo"/> is the counterpart for metadata that was
    /// declared rather than inferred.
    /// </remarks>
    public static DbParamInfo MakeInfo(IDbDataParameter p) {
        var type = p.DbType;
        ref var arr = ref SizedDbParamCache.GetCacheArray(type);
        if (Unsafe.IsNullRef(ref arr))
            return TypedDbParamCache.Get(type);
        int inferredSize = p.Size switch {
            -1 => -1,
            <= 100 => 100,
            <= 500 => 500,
            <= 4000 => 4000,
            _ => -1
        };
        return SizedDbParamCache.GetOrAdd(ref arr, type, inferredSize);
    }
    /// <summary>
    /// How a parameter binds when its metadata was declared rather than inferred, which is what the database
    /// hands back for a stored procedure's parameters.
    /// </summary>
    /// <remarks>
    /// A declaration is exact, so the size, or the precision and scale, are kept as stated instead of being
    /// widened the way <see cref="MakeInfo"/> widens a guess, and a direction other than input is carried so
    /// an output reaches the caller without being pinned by hand.
    /// </remarks>
    /// <param name="p">A parameter carrying the metadata that was declared for it.</param>
    /// <param name="inputOutputHasDefault">Whether a discovered input/output parameter may be omitted.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DbParamInfo MakeDeclaredInfo(IDbDataParameter p, bool inputOutputHasDefault = true) {
        var type = p.DbType;
        bool directed = p.Direction != ParameterDirection.Input;
        bool hasDefault = p.Direction == ParameterDirection.Output
            || (p.Direction == ParameterDirection.InputOutput && inputOutputHasDefault);
        if (p.Precision != 0 || p.Scale != 0)
            return directed
                ? DirectionalScaledDbParamCache.Get(p.Direction, type, p.Precision, p.Scale, hasDefault)
                : new ScaledDbParamCache(type, p.Precision, p.Scale);
        if (Unsafe.IsNullRef(ref SizedDbParamCache.GetCacheArray(type)))
            return directed ? DirectionalDbParamCache.Get(p.Direction, type, hasDefault) : TypedDbParamCache.Get(type);
        return directed
            ? DirectionalSizedDbParamCache.Get(p.Direction, type, p.Size, hasDefault)
            : SizedDbParamCache.Get(type, p.Size);
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
            info = DbParameterDefaults.Current.MakeInfo(p);
            return true;
        }
        info = null;
        return false;
    }
}
/// <summary>
/// Pins every parameter to driver inference and marks it settled, so the command stops trying to learn
/// provider metadata. Register it for a command type whose metadata you would rather leave to the driver.
/// </summary>
public struct ForceInferredParamCache(IDbCommand cmd) : IDbParamInfoGetter {
    /// <summary>
    /// A maker that hands back this getter for commands of type <typeparamref name="T"/>, to register in
    /// <see cref="IDbParamInfoGetter.ParamGetterMakers"/>.
    /// </summary>
    public static bool GetInfoGetterMaker<T>(IDbCommand cmd, [MaybeNullWhen(false)] out IDbParamInfoGetter getter) where T : IDbCommand {
        if (cmd is not T) {
            getter = default;
            return false;
        }
        getter = new ForceInferredParamCache(cmd);
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
        return InferredDbParamCache.ForceInferred;
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
            info = InferredDbParamCache.ForceInferred;
            return true;
        }
        info = null;
        return false;
    }
}
/// <summary>Provides fixed <see cref="DbType"/> settings for a database parameter.</summary>
public class TypedDbParamCache : DbParamInfo {
    /// <summary>Gets parameter settings for the supplied type and optional size.</summary>
    public static DbParamInfo Get(DbType type, int size = 0) {
        if (SizedDbParamCache.TryGet(type, size, out var cache))
            return cache;
        return CachedItems[(int)type];
    }
    /// <summary>Gets parameter settings for the supplied fixed size type.</summary>
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
/// Provides type and size settings for text, binary, and XML parameters. Sizes are rounded to shared limits
/// to reduce database query plan fragmentation.
/// </summary>
public class SizedDbParamCache : DbParamInfo {
    /// <summary>
    /// Gets parameter settings for the supplied type and requested size.
    /// </summary>
    public static SizedDbParamCache Get(DbType type, int size) {
        ref var arr = ref GetCacheArray(type);
        if (Unsafe.IsNullRef(ref arr))
            throw new RinkuBindingException(ErrorCodes.TypeHasNoSize, $"Type {type} does not support a custom size parameter");
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
/// Leaves the parameter type to the database provider.
/// Use <see cref="ForceInferred"/> when the command should not learn parameter details.
/// </summary>
public class InferredDbParamCache : DbParamInfo {
    /// <summary>Singleton instance of the inferred cache.</summary>
    public static readonly InferredDbParamCache Instance = new(false);
    /// <summary>Singleton instance of the inferred cache.</summary>
    public static readonly InferredDbParamCache ForceInferred = new(true);
    private InferredDbParamCache(bool isCached) : base(isCached) { }
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
