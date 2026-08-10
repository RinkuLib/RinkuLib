using System.Data;

namespace Rinku.Querying.Defaults;

/// <summary>The shipped fallback services for database-parameter binding.</summary>
public sealed class DefaultDbParameterServices : IDbParameterDefaults {
    /// <inheritdoc/>
    public DbParamInfo Inferred => InferredDbParamCache.Instance;
    /// <inheritdoc/>
    public DbParamInfo MakeInfo(IDbDataParameter parameter) => DefaultParamCache.MakeInfo(parameter);
}
