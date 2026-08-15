using System.Data;

namespace Rinku.Querying.Defaults;

/// <summary>Provides Rinku's default parameter rules and reads settings learned from a database command.</summary>
public sealed class DefaultDbParameterServices : IDbParameterDefaults {
    /// <inheritdoc/>
    public DbParamInfo Inferred => InferredDbParamCache.Instance;
    /// <inheritdoc/>
    public DbParamInfo MakeInfo(IDbDataParameter parameter) => DefaultParamCache.MakeInfo(parameter);
}
