namespace Rinku.Tracking.Runtime;

/// <summary>Creates tracking lists from original values.</summary>
public static class RuntimeMaterializationExtensions
{
    /// <summary>Creates a tracking list with the default runtime contract.</summary>
    public static TrackingList<IRuntimeTrackingItem<TOriginal>> ToTrackingList<TOriginal>(
        this IEnumerable<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<IRuntimeTrackingItem<TOriginal>>? comparer = null,
        ITrackingListContext<IRuntimeTrackingItem<TOriginal>>? context = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> registration = RuntimeTracking.Default<TOriginal>();
        return Materialize(originals, registration, initialCapacity, comparer, context ?? registration.CreateContext(originals));
    }

    /// <summary>Creates a default contract list with custom runtime options.</summary>
    public static TrackingList<IRuntimeTrackingItem<TOriginal>> ToTrackingList<TOriginal>(
        this IEnumerable<TOriginal> originals,
        RuntimeTrackingOptions<TOriginal> options,
        int initialCapacity = 0,
        IEqualityComparer<IRuntimeTrackingItem<TOriginal>>? comparer = null,
        ITrackingListContext<IRuntimeTrackingItem<TOriginal>>? context = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> registration = options.GetRegistration<IRuntimeTrackingItem<TOriginal>>();
        return Materialize(originals, registration, initialCapacity, comparer, context ?? registration.CreateContext(originals));
    }

    /// <summary>Creates a tracking list with a generated interface contract.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(
        this IEnumerable<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<TEdit>? comparer = null,
        ITrackingListContext<TEdit>? context = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = RuntimeTrackingContractCache<TOriginal, TEdit>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer, context ?? registration.CreateContext(originals));
    }

    /// <summary>Creates an interface contract list with custom runtime options.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(
        this IEnumerable<TOriginal> originals,
        RuntimeTrackingOptions<TOriginal> options,
        int initialCapacity = 0,
        IEqualityComparer<TEdit>? comparer = null,
        ITrackingListContext<TEdit>? context = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = options.GetRegistration<TEdit>();
        return Materialize(originals, registration, initialCapacity, comparer, context ?? registration.CreateContext(originals));
    }

    private static TrackingList<TEdit> Materialize<TOriginal, TEdit>(
        IEnumerable<TOriginal> originals,
        RuntimeTrackingRegistration<TOriginal, TEdit> registration,
        int initialCapacity,
        IEqualityComparer<TEdit>? comparer,
        ITrackingListContext<TEdit> context)
    {
        int capacity = initialCapacity;
        if (originals.TryGetNonEnumeratedCount(out int count) && count > capacity) capacity = count;
        var list = new TrackingList<TEdit>(capacity, comparer, context);
        int sourceIndex = 0;
        IRuntimeTrackingListMaterializationContext<TEdit>? materializationContext = context as IRuntimeTrackingListMaterializationContext<TEdit>;
        foreach (TOriginal original in originals)
        {
            TEdit item = registration.Create(original);
            list.AddInitial(item);
            materializationContext?.TrackInitial(item, sourceIndex++);
        }
        return list;
    }

    private static void EnsureInterface<TEdit>()
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"{typeof(TEdit)} is concrete. Automatic TOriginal -> TEdit materialization is only provided for runtime-generated interface contracts; construct TrackingList<{typeof(TEdit).Name}> directly when you own the concrete type/factory.");
    }
}
