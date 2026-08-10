using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>
/// A reusable <see cref="TypeParsingInfo"/> implementation core for values negotiated from one result
/// column. It applies the ordinary name, sequential-read, reuse, and fallback rules; implementations only
/// decide whether a candidate column can produce a plan.
/// </summary>
public abstract class ScalarTypeParsingInfo : TypeParsingInfo {
    /// <inheritdoc/>
    public sealed override DbItemPlan? TryGetParser(Type currentClosedType, RecursiveInfo previousUsages,
        ParamInfo paramInfo, ColumnInfo[] columns, ColModifier colModifier, ref ColumnUsage colUsage,
        MethodCtorInfo.AdditionalFlags callerFlags = default) {
        paramInfo.UpdateColModifier(ref colModifier);
        var flags = colModifier.Flags;
        if (colModifier.SwapFirstAt >= 0 && colUsage.NbClaims == colModifier.SwapFirstAt)
            flags |= colModifier.SwapFirstFlags;
        bool canReuse = flags.HasFlag(UsageFlags.CanReuse);
        if (flags.HasFlag(UsageFlags.SequentialRead) && !flags.HasFlag(UsageFlags.RemoveSequentialRead)) {
            int ordinal = colUsage.LastIndexUsed + 1;
            if (ordinal < columns.Length && (canReuse || !colUsage.IsUsed(ordinal))) {
                var column = columns[ordinal];
                if (paramInfo.NameComparer.Match(column.Name, colModifier.Comparers)) {
                    var plan = TryCreatePlan(currentClosedType, previousUsages.LatestUsedType, paramInfo, column, ordinal);
                    if (plan is not null) {
                        if (canReuse)
                            colUsage.Reuse(ordinal);
                        else
                            colUsage.Use(ordinal);
                        return plan;
                    }
                }
            }
            return paramInfo.FallbackTryGetParser(currentClosedType);
        }
        for (int ordinal = 0; ordinal < columns.Length; ordinal++) {
            if (!canReuse && colUsage.IsUsed(ordinal))
                continue;
            var column = columns[ordinal];
            if (!paramInfo.NameComparer.Match(column.Name, colModifier.Comparers))
                continue;
            var plan = TryCreatePlan(currentClosedType, previousUsages.LatestUsedType, paramInfo, column, ordinal);
            if (plan is null)
                continue;
            if (canReuse)
                colUsage.Reuse(ordinal);
            else
                colUsage.Use(ordinal);
            return plan;
        }
        return paramInfo.FallbackTryGetParser(currentClosedType);
    }

    /// <summary>
    /// Tries to create the plan for one name-compatible candidate column. Returning <see langword="null"/>
    /// lets negotiation continue with the next column.
    /// </summary>
    /// <param name="targetType">The closed target type requested by the containing mapping.</param>
    /// <param name="parentType">The type containing the value being read.</param>
    /// <param name="parameter">The target member or constructor-parameter rules.</param>
    /// <param name="column">The candidate schema column.</param>
    /// <param name="ordinal">The candidate's zero-based ordinal.</param>
    protected abstract DbItemPlan? TryCreatePlan(Type targetType, Type parentType, ParamInfo parameter,
        ColumnInfo column, int ordinal);
}

/// <summary>
/// A typed single-column parsing implementation. The same registration also accepts <c>Nullable&lt;T&gt;</c>
/// when <typeparamref name="T"/> is a value type; the implementation sees that closed target during plan
/// creation and decides how to emit its wrapper.
/// </summary>
public abstract class ScalarTypeParsingInfo<T> : ScalarTypeParsingInfo {
    /// <inheritdoc/>
    public sealed override void ValidateCanUseType(Type targetType) {
        if ((Nullable.GetUnderlyingType(targetType) ?? targetType) != typeof(T))
            throw new RinkuConfigurationException(ErrorCodes.TypeNotUsableByInfo,
                $"{GetType().Name} only handles {typeof(T)}");
    }
}
