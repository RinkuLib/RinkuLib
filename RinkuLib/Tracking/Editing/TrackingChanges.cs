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

}

/// <summary>Provides helpers for tracked changes.</summary>
public static class TrackingChangeExtensions
{
    /// <summary>Gets the current changes.</summary>
    public static IEnumerable<TrackingChange> GetChanges(this ITrackingChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        for (int i = 0; i < changes.TrackedMemberCount; i++)
            if (changes.TryGetChange(i, out TrackingChange change)) yield return change;
    }

    /// <summary>Returns whether any member has changed.</summary>
    public static bool HasChanges(this ITrackingChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        for (int i = 0; i < changes.TrackedMemberCount; i++)
            if (changes.TryGetChange(i, out _)) return true;
        return false;
    }
}
