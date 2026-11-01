using Rinku.Tracking.Binding;

namespace Rinku.Tracking.Runtime;

/// <summary>Creates tracking lists from original values.</summary>
/// <remarks>When the source is an <see cref="IList{T}"/>, successful confirmations are applied back to that list.</remarks>
public static class RuntimeMaterializationExtensions
{
    /// <summary>Materializes streamed originals directly into an independently owned tracking list.</summary>
    public static async Task<TrackingList<TEdit>> ToTrackingListAsync<TOriginal, TEdit>(this IAsyncEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal>? options = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        var registration = options is null ? RuntimeTrackingContractCache<TOriginal, TEdit>.Registration : options.GetRegistration<TEdit>();
        var list = new TrackingList<TEdit>(newItemFactory: registration.CreateNew);
        try {
            await foreach (TOriginal original in originals.WithCancellation(cancellationToken).ConfigureAwait(false))
                list.AddInitial(registration.Create(original));
            cancellationToken.ThrowIfCancellationRequested();
            return list;
        }
        catch {
            list.Dispose();
            throw;
        }
    }

    /// <summary>Creates a tracking list with the default runtime contract.</summary>
    public static TrackingList<ITrackingItem<TOriginal>> ToTrackingList<TOriginal>(this IEnumerable<TOriginal> originals, int initialCapacity = 0, IEqualityComparer<ITrackingItem<TOriginal>>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        RuntimeTrackingRegistration<TOriginal, ITrackingItem<TOriginal>> registration = RuntimeTracking.Default<TOriginal>();
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    /// <summary>Creates a default contract list with custom runtime options.</summary>
    public static TrackingList<ITrackingItem<TOriginal>> ToTrackingList<TOriginal>(this IEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal> options, int initialCapacity = 0, IEqualityComparer<ITrackingItem<TOriginal>>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        RuntimeTrackingRegistration<TOriginal, ITrackingItem<TOriginal>> registration = options.GetRegistration<ITrackingItem<TOriginal>>();
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    /// <summary>Creates a tracking list with a generated interface contract.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = RuntimeTrackingContractCache<TOriginal, TEdit>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    /// <summary>Creates an interface contract list with custom runtime options.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal> options, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = options.GetRegistration<TEdit>();
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    private static TrackingList<TEdit> Materialize<TOriginal, TEdit>(IEnumerable<TOriginal> originals, RuntimeTrackingRegistration<TOriginal, TEdit> registration, int initialCapacity, IEqualityComparer<TEdit>? comparer)
    {
        int capacity = initialCapacity;
        if (originals.TryGetNonEnumeratedCount(out int count) && count > capacity) capacity = count;
        Func<TEdit> newItemFactory = registration.CreateNew;
        var list = new TrackingList<TEdit>(capacity, comparer, newItemFactory);
        foreach (TOriginal original in originals)
        {
            TEdit item = registration.Create(original);
            list.AddInitial(item);
        }
        if (originals is IList<TOriginal> target) list.BindBack(target);
        return list;
    }

    private static void EnsureInterface<TEdit>()
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"{typeof(TEdit)} is concrete. Automatic TOriginal -> TEdit materialization is only provided for runtime-generated interface contracts; construct TrackingList<{typeof(TEdit).Name}> directly when you own the concrete type/factory.");
    }
}
