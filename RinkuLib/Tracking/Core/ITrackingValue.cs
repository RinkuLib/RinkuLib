namespace Rinku.Tracking;

public interface ITrackingValue<T> : IEquatable<T>
{
    T Copy();
}
