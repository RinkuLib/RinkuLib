namespace Rinku.Tracking.Runtime;

/// <summary>Chooses how a nested edit is accepted.</summary>
public enum NestedEditMode
{
    /// <summary>Copies changed members into the accepted object.</summary>
    InPlace,
    /// <summary>Replaces the accepted nested value.</summary>
    Replacement
}
