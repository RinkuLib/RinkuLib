using System.ComponentModel;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Binding;

/// <summary>Creates binding tracking lists from original values.</summary>
public static class RuntimeBindingMaterializationExtensions
{
    /// <summary>Creates a binding list with the default runtime contract.</summary>
    public static BindingTrackingList<IRuntimeTrackingItem<TOriginal>> ToBindingList<TOriginal>(
        this IEnumerable<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<IRuntimeTrackingItem<TOriginal>>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> registration = RuntimeBindingCache<TOriginal, IRuntimeTrackingItem<TOriginal>>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer, registration.CreateContext(originals));
    }

    /// <summary>Creates a binding list with a generated interface contract.</summary>
    public static BindingTrackingList<TEdit> ToBindingList<TOriginal, TEdit>(
        this IEnumerable<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = RuntimeBindingCache<TOriginal, TEdit>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer, registration.CreateContext(originals));
    }

    /// <summary>Creates a source-aware binding list with the default runtime contract.</summary>
    public static BindingTrackingList<IRuntimeTrackingItem<TOriginal>> ToBindingList<TOriginal>(
        this BindingList<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<IRuntimeTrackingItem<TOriginal>>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        RuntimeTrackingRegistration<TOriginal, IRuntimeTrackingItem<TOriginal>> registration = RuntimeBindingCache<TOriginal, IRuntimeTrackingItem<TOriginal>>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer, registration.CreateContext(originals));
    }

    /// <summary>Creates a source-aware binding list with a generated interface contract.</summary>
    public static BindingTrackingList<TEdit> ToBindingList<TOriginal, TEdit>(
        this BindingList<TOriginal> originals,
        int initialCapacity = 0,
        IEqualityComparer<TEdit>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        EnsureInterface<TEdit>();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = RuntimeBindingCache<TOriginal, TEdit>.Registration;
        return Materialize(originals, registration, initialCapacity, comparer, registration.CreateContext(originals));
    }

    /// <summary>Creates a binding list with custom runtime options.</summary>
    public static BindingTrackingList<TEdit> ToBindingList<TOriginal, TEdit>(
        this IEnumerable<TOriginal> originals,
        RuntimeTrackingOptions<TOriginal> options,
        int initialCapacity = 0,
        IEqualityComparer<TEdit>? comparer = null,
        ITrackingListContext<TEdit>? context = null)
    {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        EnsureInterface<TEdit>();
        RuntimeTrackingOptions<TOriginal> bindingOptions = options.CloneUnfrozen().Binding();
        RuntimeTrackingRegistration<TOriginal, TEdit> registration = bindingOptions.GetRegistration<TEdit>();
        return Materialize(originals, registration, initialCapacity, comparer, context ?? registration.CreateContext(originals));
    }

    private static BindingTrackingList<TEdit> Materialize<TOriginal, TEdit>(
        IEnumerable<TOriginal> originals,
        RuntimeTrackingRegistration<TOriginal, TEdit> registration,
        int initialCapacity,
        IEqualityComparer<TEdit>? comparer,
        ITrackingListContext<TEdit> context)
    {
        int capacity = initialCapacity;
        if (originals.TryGetNonEnumeratedCount(out int count) && count > capacity) capacity = count;
        var list = new BindingTrackingList<TEdit>(capacity, comparer, context);
        list.Configure(TypeDescriptor.GetProperties(registration.GeneratedType), typeof(TEdit).Name);
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
