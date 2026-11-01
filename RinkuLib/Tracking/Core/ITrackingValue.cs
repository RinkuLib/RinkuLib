namespace Rinku.Tracking;

/// <summary>Represents a tracked value that can produce an independent copy.</summary>
public interface ITrackingValue<T> : IEquatable<T>
{
    /// <summary>Creates an independent copy of the value.</summary>
    T Copy();
}
