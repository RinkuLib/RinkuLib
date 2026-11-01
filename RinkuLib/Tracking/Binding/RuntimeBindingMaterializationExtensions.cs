using System.Collections;
using System.ComponentModel;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Binding;

/// <summary>Creates binding tracking lists from original values.</summary>
/// <remarks>When the source is an <see cref="IList{T}"/>, successful confirmations are applied back to that list.</remarks>
public static class RuntimeBindingMaterializationExtensions
{
    /// <summary>Materializes streamed originals directly into an independently owned binding tracking list.</summary>
    public static async Task<BindingTrackingList<TEdit>> ToBindingListAsync<TOriginal, TEdit>(this IAsyncEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal>? options = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        var registration = options is null ? RuntimeBindingCache<TOriginal, TEdit>.Registration : options.GetBindingRegistration<TEdit>();
        var list = new BindingTrackingList<TEdit>(newItemFactory: registration.CreateNew);
        list.Configure(TypeDescriptor.GetProperties(registration.GeneratedType), typeof(TEdit).Name);
        list.ConfigureBaselineFactory<TOriginal>(registration.Create);
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

    /// <summary>Materializes one original value as a generated tracking item.</summary>
    public static ITrackingItem<TOriginal> ToBindingItem<TOriginal>(this TOriginal original) => RuntimeBindingCache<TOriginal, ITrackingItem<TOriginal>>.Registration.Create(original);

    /// <summary>Creates a binding list with the default runtime contract.</summary>
    public static BindingTrackingList<ITrackingItem<TOriginal>> ToBindingList<TOriginal>(this IEnumerable<TOriginal> originals, int initialCapacity = 0, IEqualityComparer<ITrackingItem<TOriginal>>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        RuntimeTrackingRegistration<TOriginal, ITrackingItem<TOriginal>> registration = RuntimeBindingCache<TOriginal, ITrackingItem<TOriginal>>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    /// <summary>Creates a binding list with a generated interface contract.</summary>
    public static BindingTrackingList<TEdit> ToBindingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = RuntimeBindingCache<TOriginal, TEdit>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    /// <summary>Creates a binding list with custom runtime options.</summary>
    public static BindingTrackingList<TEdit> ToBindingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal> options, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = options.GetBindingRegistration<TEdit>();
        return Materialize(originals, registration, initialCapacity, comparer);
    }

    private static BindingTrackingList<TEdit> Materialize<TOriginal, TEdit>(IEnumerable<TOriginal> originals, RuntimeTrackingRegistration<TOriginal, TEdit> registration, int initialCapacity, IEqualityComparer<TEdit>? comparer)
    {
        int capacity = initialCapacity;
        if (originals.TryGetNonEnumeratedCount(out int count) && count > capacity) capacity = count;
        Func<TEdit> newItemFactory = registration.CreateNew;
        var list = new BindingTrackingList<TEdit>(capacity, comparer, newItemFactory);
        list.Configure(TypeDescriptor.GetProperties(registration.GeneratedType), typeof(TEdit).Name);
        list.ConfigureBaselineFactory<TOriginal>(registration.Create);
        foreach (TOriginal original in originals)
        {
            TEdit item = registration.Create(original);
            list.AddInitial(item);
        }
        list.BindSource(originals);
        return list;
    }

    private static void EnsureInterface<TEdit>()
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"{typeof(TEdit)} is concrete. Automatic TOriginal -> TEdit materialization is only provided for runtime-generated interface contracts.");
    }
}

internal static class RuntimeBindingCache<TOriginal, TEdit>
{
    internal static readonly RuntimeTrackingRegistration<TOriginal, TEdit> Registration = Build();

    private static RuntimeTrackingRegistration<TOriginal, TEdit> Build()
    {
        RuntimeTrackingOptions<TOriginal> options = RuntimeTracking.CreateOptions<TOriginal, TEdit>();
        options.Binding();
        return options.GetRegistration<TEdit>();
    }
}
