namespace Rinku.Tracking.Runtime;

/// <summary>Creates options and registrations for generated tracking types.</summary>
public static class RuntimeTracking
{
    /// <summary>Creates default runtime tracking options.</summary>
    public static RuntimeTrackingOptions<TOriginal> CreateOptions<TOriginal>() => new();

    /// <summary>Creates runtime tracking options for an interface contract.</summary>
    public static RuntimeTrackingOptions<TOriginal> CreateOptions<TOriginal, TEdit>()
    {
        var options = new RuntimeTrackingOptions<TOriginal>();
        options.Apply<TEdit>();
        return options;
    }

    /// <summary>Gets the shared default registration.</summary>
    public static RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> Default<TOriginal>()
        => RuntimeTrackingDefaultCache<TOriginal>.Registration;
}

internal static class RuntimeTrackingDefaultCache<TOriginal>
{
    internal static readonly RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> Registration = Build();

    private static RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> Build()
    {
        RuntimeTrackingOptions<TOriginal> options = RuntimeTracking.CreateOptions<TOriginal, IRuntimeTrackingItem<TOriginal>>();
        return options.GetRegistration<IRuntimeTrackingItem<TOriginal>>();
    }
}

internal static class RuntimeTrackingContractCache<TOriginal, TEdit>
{
    internal static readonly RuntimeTrackingRegistration<TOriginal, TEdit> Registration = Build();

    private static RuntimeTrackingRegistration<TOriginal, TEdit> Build()
    {
        RuntimeTrackingOptions<TOriginal> options = RuntimeTracking.CreateOptions<TOriginal, TEdit>();
        return options.GetRegistration<TEdit>();
    }
}
