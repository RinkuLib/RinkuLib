using System;
using System.Collections.Generic;
using System.Linq;

namespace Rinku.Tracking;

/// <summary>Provides tracking item and list extension methods.</summary>
public static class TrackingExtensions {
    /// <summary>Creates a tracking item with a selector.</summary>
    public static TEdit ToTrackingItem<TOriginal, TEdit>(this TOriginal original, Func<TOriginal, TEdit> selector) {
        ArgumentNullException.ThrowIfNull(selector);
        TEdit item = selector(original);
        return item is null ? throw new InvalidOperationException("The tracking-item selector returned null.") : item;
    }

    // Unified cached path: handwritten IFromOriginal<TOriginal,TEdit> or generated runtime contract.
    /// <summary>Creates a tracking item.</summary>
    public static TEdit ToTrackingItem<TOriginal, TEdit>(this TOriginal original)
        => TrackingItemMaterializer<TOriginal, TEdit>.Create(original);

    /// <summary>Creates a tracking list.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals,
        int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null) {
        ArgumentNullException.ThrowIfNull(originals);
        int capacity = originals.TryGetNonEnumeratedCount(out int count) ? Math.Max(count, initialCapacity) : initialCapacity;
        Func<TOriginal, TEdit> create = TrackingItemMaterializer<TOriginal, TEdit>.Creator;
        var list = new TrackingList<TEdit>(capacity, comparer);
        TrackingItemMaterializer<TOriginal, TEdit>.ConfigureList(list);
        foreach (TOriginal original in originals) {
            TEdit item = create(original);
            if (item is null) throw new InvalidOperationException("The tracking-item materializer returned null.");
            list.AddInitial(item);
        }
        return list;
    }

    /// <summary>Creates a tracking list with a selector.</summary>
    public static TrackingList<TEdit> ToTrackingList<TOriginal, TEdit>(this IEnumerable<TOriginal> originals,
        Func<TOriginal, TEdit> selector, int initialCapacity = 0, IEqualityComparer<TEdit>? comparer = null) {
        ArgumentNullException.ThrowIfNull(originals);
        ArgumentNullException.ThrowIfNull(selector);
        int capacity = originals.TryGetNonEnumeratedCount(out int count) ? Math.Max(count, initialCapacity) : initialCapacity;
        var list = new TrackingList<TEdit>(capacity, comparer);
        foreach (TOriginal original in originals) {
            TEdit item = selector(original);
            if (item is null) throw new InvalidOperationException("The tracking-item selector returned null.");
            list.AddInitial(item);
        }
        return list;
    }

    /// <summary>Gets whether a list has item or structural changes.</summary>
    public static bool HasChanges<T>(this TrackingList<T> list) {
        ArgumentNullException.ThrowIfNull(list);
        if (list.HasStructuralChanges) return true;
        if (!TrackingItemCapabilities<T>.IsEditable) return false;
        for (int i = 0; i < list.Count; i++) if (TrackingItemCapabilities<T>.IsEditing(list[i])) return true;
        return false;
    }

    /// <summary>Starts edits for all supplied items.</summary>
    public static bool EnsureEdits(this IEnumerable<IEditable> items) {
        bool changed = false;
        foreach (IEditable item in items) changed |= item.EnsureEditing();
        return changed;
    }

    /// <summary>Commits edits for all supplied items.</summary>
    public static bool CommitEdits(this IEnumerable<IEditable> items) {
        bool changed = false;
        foreach (IEditable item in items) changed |= item.CommitEdit();
        return changed;
    }

    /// <summary>Cancels edits for all supplied items.</summary>
    public static bool CancelEdits(this IEnumerable<IEditable> items) {
        bool changed = false;
        foreach (IEditable item in items) changed |= item.CancelEdit();
        return changed;
    }

    /// <summary>Returns the available original values.</summary>
    public static IEnumerable<TOriginal> Originals<TOriginal>(this IEnumerable<ITrackingItem<TOriginal>> items) {
        foreach (ITrackingItem<TOriginal> item in items)
            if (item.TryGetOriginal(out TOriginal? original)) yield return original!;
    }
}
