using System.Diagnostics.CodeAnalysis;

namespace Rinku.Tracking;

// Stable, non-generic origin capability. Anything that can commit an edit must expose this.
/// <summary>Reports whether a tracked value has an accepted original.</summary>
public interface IHasOriginal {
    /// <summary>Gets whether an original value is available.</summary>
    bool HasOriginal { get; }
}

// Tracking-item marker kept for APIs that want the semantic capability, not just the boolean.
/// <summary>Marks a value as a tracking item.</summary>
public interface ITrackingItem : IHasOriginal { }

/// <summary>Provides typed original-value access for a tracking item.</summary>
public interface ITrackingItem<TOriginal> : ITrackingItem {
    /// <summary>Copies the original value when one is available.</summary>
    bool TryGetOriginal([MaybeNullWhen(false)] out TOriginal original);
}

// Materialization capability. TEdit may be either a class or a struct.
/// <summary>Creates a tracking value from an original value.</summary>
public interface IFromOriginal<TOriginal, TEdit> {
    /// <summary>Creates the edit value.</summary>
    static abstract TEdit Create(TOriginal original);
}

// CommitEdit changes whether a newly-created interaction has an original, therefore the origin
// capability is part of the editing contract itself and can never be absent from an editable item.
/// <summary>Provides edit state operations.</summary>
public interface IEditable : IHasOriginal {
    /// <summary>Gets whether an edit is active.</summary>
    bool IsEditing { get; }
    /// <summary>Starts an edit when none is active.</summary>
    bool EnsureEditing();
    /// <summary>Accepts the active edit.</summary>
    bool CommitEdit();
    /// <summary>Cancels the active edit.</summary>
    bool CancelEdit();
}

/// <summary>Combines typed original access and edit state operations.</summary>
public interface IEditableTrackingItem<TOriginal> : ITrackingItem<TOriginal>, IEditable { }
