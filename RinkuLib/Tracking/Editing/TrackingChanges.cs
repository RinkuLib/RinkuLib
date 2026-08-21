using System.Collections;

namespace Rinku.Tracking;

/// <summary>Describes one changed member.</summary>
public readonly record struct TrackingChange
{
    /// <summary>Creates a member change.</summary>
    public TrackingChange(string name, object? originalValue, object? value)
    {
        Name = name;
        OriginalValue = originalValue;
        Value = value;
    }

    /// <summary>Gets the member name.</summary>
    public string Name { get; }
    /// <summary>Gets the accepted value.</summary>
    public object? OriginalValue { get; }
    /// <summary>Gets the edited value.</summary>
    public object? Value { get; }
}

/// <summary>Allocation-free enumerable over actual snapshot differences.</summary>
public interface ITrackingChanges
{
    /// <summary>Gets the number of members that may be tracked.</summary>
    int TrackedMemberCount { get; }
    /// <summary>Tries to get the change at a member index.</summary>
    bool TryGetChange(int memberIndex, out TrackingChange change);

    /// <summary>Gets the current changes.</summary>
    TrackingChangeEnumerable GetChanges() => new(this);
}

/// <summary>Enumerates changed members.</summary>
public readonly struct TrackingChangeEnumerable : IEnumerable<TrackingChange>
{
    private readonly ITrackingChanges _source;

    internal TrackingChangeEnumerable(ITrackingChanges source) => _source = source;

    /// <summary>Gets an enumerator.</summary>
    public Enumerator GetEnumerator() => new(_source);
    IEnumerator<TrackingChange> IEnumerable<TrackingChange>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates changed members.</summary>
    public struct Enumerator : IEnumerator<TrackingChange>
    {
        private readonly ITrackingChanges _source;
        private int _index;
        private TrackingChange _current;

        internal Enumerator(ITrackingChanges source)
        {
            _source = source;
            _index = -1;
            _current = default;
        }

        /// <inheritdoc/>
        public readonly TrackingChange Current => _current;
        readonly object IEnumerator.Current => _current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            while (++_index < _source.TrackedMemberCount)
            {
                if (!_source.TryGetChange(_index, out TrackingChange change)) continue;
                _current = change;
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _index = -1;
            _current = default;
        }

        /// <inheritdoc/>
        public readonly void Dispose() { }
    }
}

/// <summary>Provides helpers for tracked changes.</summary>
public static class TrackingChangeExtensions
{
    /// <summary>Returns whether any member has changed.</summary>
    public static bool HasChanges(this ITrackingChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        for (int i = 0; i < changes.TrackedMemberCount; i++)
            if (changes.TryGetChange(i, out _)) return true;
        return false;
    }
}
