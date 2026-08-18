namespace Rinku.Tracking.Runtime;

/// <summary>Creates runtime tracking configuration.</summary>
public static class RuntimeTracking {
    /// <summary>Creates runtime tracking options.</summary>
    public static RuntimeTrackingOptions<TOriginal> Options<TOriginal>(bool includeDefaultMembers = true)
        => new(includeDefaultMembers);

    /// <summary>Creates options for a runtime contract.</summary>
    public static RuntimeTrackingOptions<TOriginal> ContractOptions<TOriginal, TEdit>()
        where TEdit : class, IRuntimeTrackingItem<TOriginal>
        => RuntimeTrackingContract<TOriginal, TEdit>.BuildOptions();
}
