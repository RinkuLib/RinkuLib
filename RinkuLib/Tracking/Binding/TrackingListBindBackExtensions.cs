using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Rinku.Tracking.Binding;

/// <summary>Synchronizes successfully confirmed tracking operations with external lists.</summary>
public static class TrackingListBindBackExtensions
{
    /// <summary>
    /// Reflects confirmed operations into a list of originals exposed by the runtime tracking items.
    /// </summary>
    public static IDisposable BindBack<T, TOriginal>(this ITrackingConfirmations<T> tracking, IList<TOriginal> target)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        ArgumentNullException.ThrowIfNull(target);
        if (typeof(T).IsValueType)
            throw new NotSupportedException("Original-value list binding requires reference-type tracking items.");
        return new OriginalListBindBack<T, TOriginal>(tracking, target);
    }

    private sealed class OriginalListBindBack<T, TOriginal> : IDisposable
    {
        private readonly IList<TOriginal> _target;
        private readonly BindingList<TOriginal>? _bindingTarget;
        private readonly Dictionary<object, TOriginal> _originals = new(ReferenceEqualityComparer.Instance);
        private IDisposable? _subscription;

        internal OriginalListBindBack(ITrackingConfirmations<T> tracking, IList<TOriginal> target)
        {
            _target = target;
            _bindingTarget = target as BindingList<TOriginal>;

            for (int i = 0; i < tracking.Count; i++)
            {
                T item = tracking[i];
                if (!TryOriginal(item, out TOriginal? original)) continue;
                int targetIndex = FindIndex(original, i);
                if (targetIndex >= 0) _originals[Key(item)] = _target[targetIndex];
            }

            _subscription = tracking.SubscribeConfirmations(Added, Updated, Deleted);
        }

        private void Added(int index, T item)
        {
            if (!TryOriginal(item, out TOriginal? original))
                throw MissingOriginal();
            EnsureWritable("insert");

            if ((uint)index <= (uint)_target.Count)
                _target.Insert(index, original);
            else
                _target.Add(original);
            _originals[Key(item)] = original;
        }

        private void Updated(int index, T item)
        {
            if (!TryOriginal(item, out TOriginal? accepted))
                throw MissingOriginal();

            object key = Key(item);
            TOriginal expected = _originals.TryGetValue(key, out TOriginal? original) ? original : accepted;
            int targetIndex = FindIndex(expected, index);
            if (targetIndex < 0)
            {
                if (item is ITrackingListNewState { IsNew: true }) return;
                throw new InvalidOperationException("The original item could not be found in the BindBack target.");
            }

            TOriginal current = _target[targetIndex];
            if (Same(current, accepted))
            {
                if (_bindingTarget is not null && accepted is not INotifyPropertyChanged)
                    _bindingTarget.ResetItem(targetIndex);
            }
            else
            {
                EnsureWritable("update");
                _target[targetIndex] = accepted;
            }

            _originals[key] = accepted;
        }

        private void Deleted(T item)
        {
            object key = Key(item);
            if (!_originals.TryGetValue(key, out TOriginal? original) && !TryOriginal(item, out original))
                throw MissingOriginal();

            int targetIndex = _target.IndexOf(original);
            if (targetIndex < 0)
                throw new InvalidOperationException("The original item could not be found in the BindBack target.");
            EnsureWritable("delete");
            _target.RemoveAt(targetIndex);
            _originals.Remove(key);
        }

        private int FindIndex(TOriginal item, int indexHint)
        {
            if ((uint)indexHint < (uint)_target.Count && Same(_target[indexHint], item))
                return indexHint;
            return _target.IndexOf(item);
        }

        private void EnsureWritable(string operation)
        {
            if (_target.IsReadOnly)
                throw new NotSupportedException($"The BindBack target does not support {operation} operations.");
        }

        private static bool TryOriginal(T item, [MaybeNullWhen(false)] out TOriginal original)
        {
            if (item is IOriginal<TOriginal> source && source.TryGetOriginal(out original)) return true;
            original = default;
            return false;
        }

        private static bool Same(TOriginal left, TOriginal right)
            => typeof(TOriginal).IsValueType
                ? EqualityComparer<TOriginal>.Default.Equals(left, right)
                : ReferenceEquals(left, right);

        private static object Key(T item)
            => (object?)item ?? throw new InvalidOperationException("A null tracking item cannot participate in BindBack.");

        private static InvalidOperationException MissingOriginal()
            => new($"The confirmed {typeof(T)} item does not expose an original {typeof(TOriginal)} value.");

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            _originals.Clear();
        }
    }
}
