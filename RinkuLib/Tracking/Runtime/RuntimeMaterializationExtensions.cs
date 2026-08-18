using System;
using System.Collections.Generic;
using System.Linq;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking;

/// <summary>Provides runtime tracking materialization extension methods.</summary>
public static class RuntimeMaterializationExtensions {
    // Default fully dynamic/UI-friendly shape: editable + original + name/index access + notifications.
    /// <summary>Creates a default dynamic tracking item.</summary>
    public static IRuntimeDynamicTrackingItem<TOriginal> ToTrackingItem<TOriginal>(this TOriginal original)
        => RuntimeTrackingDefaultShapeCache<TOriginal>.Registration.Create(original);

    /// <summary>Creates a dynamic tracking item with options.</summary>
    public static IRuntimeDynamicTrackingItem<TOriginal> ToTrackingItem<TOriginal>(this TOriginal original,
        RuntimeTrackingOptions<TOriginal> options) {
        ArgumentNullException.ThrowIfNull(options);
        return options.GetRegistration<IRuntimeDynamicTrackingItem<TOriginal>>().Create(original);
    }

    /// <summary>Creates a typed tracking item with options.</summary>
    public static TEdit ToTrackingItem<TOriginal, TEdit>(this TOriginal original, RuntimeTrackingOptions<TOriginal> options)
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        ArgumentNullException.ThrowIfNull(options);
        return options.GetRegistration<TEdit>().Create(original);
    }

    /// <summary>Creates a typed tracking item with configuration.</summary>
    public static TEdit ToTrackingItem<TOriginal, TEdit>(this TOriginal original,
        Action<RuntimeTrackingOptions<TOriginal>> configure)
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        ArgumentNullException.ThrowIfNull(configure);
        RuntimeTrackingOptions<TOriginal> options = RuntimeTrackingContract<TOriginal, TEdit>.BuildOptions();
        configure(options);
        return options.GetRegistration<TEdit>().Create(original);
    }

    /// <summary>Creates a dynamic tracking list.</summary>
    public static TrackingList<IRuntimeDynamicTrackingItem<TOriginal>> ToTrackingList<TOriginal>(
        this IEnumerable<TOriginal> originals, int initialCapacity = 0,
        IEqualityComparer<IRuntimeDynamicTrackingItem<TOriginal>>? comparer = null) {
        ArgumentNullException.ThrowIfNull(originals);
        return Materialize(originals, RuntimeTrackingDefaultShapeCache<TOriginal>.Registration, initialCapacity, comparer);
    }

    /// <summary>Creates a dynamic tracking list with options.</summary>
    public static TrackingList<IRuntimeDynamicTrackingItem<TOriginal>> ToTrackingList<TOriginal>(
        this IEnumerable<TOriginal> originals, RuntimeTrackingOptions<TOriginal> options, int initialCapacity = 0,
        IEqualityComparer<IRuntimeDynamicTrackingItem<TOriginal>>? comparer = null) {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        return Materialize(originals, options.GetRegistration<IRuntimeDynamicTrackingItem<TOriginal>>(), initialCapacity, comparer);
    }

    /// <summary>Creates a typed tracking list with options.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals,
        RuntimeTrackingOptions<TOriginal> options, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(options);
        return Materialize(originals, options.GetRegistration<TEdit>(), initialCapacity, comparer);
    }

    /// <summary>Creates a typed tracking list with configuration.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals,
        Action<RuntimeTrackingOptions<TOriginal>> configure, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null)
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(configure);
        RuntimeTrackingOptions<TOriginal> options = RuntimeTrackingContract<TOriginal, TEdit>.BuildOptions();
        configure(options);
        return Materialize(originals, options.GetRegistration<TEdit>(), initialCapacity, comparer);
    }

    private static TrackingList<TEdit> Materialize<TOriginal, TEdit>(IEnumerable<TOriginal> originals,
        RuntimeTrackingRegistration<TOriginal, TEdit> registration, int initialCapacity, IEqualityComparer<TEdit>? comparer)
        where TEdit : class, IRuntimeTrackingItem<TOriginal> {
        int capacity = originals.TryGetNonEnumeratedCount(out int count) ? Math.Max(count, initialCapacity) : initialCapacity;
        var list = new TrackingList<TEdit>(capacity, comparer);
        list.ConfigureBinding(registration.CanCreateNew ? registration.CreateNew : null, registration.GetProperties, typeof(TEdit).Name);
        foreach (TOriginal original in originals) list.AddInitial(registration.Create(original));
        return list;
    }
}
