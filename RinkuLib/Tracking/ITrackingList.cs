using System.Diagnostics.CodeAnalysis;

namespace Rinku.Tracking;
/// <summary>Exposes an item count, the common base of the tracking-list interfaces.</summary>
public interface ICount {
    /// <summary>The number of items in the list.</summary>
    public int ItemCount { get; }
}
/// <summary>A list that remembers which items were removed and keeps each item's original value.</summary>
public interface ITrackingList<T> : ICount {
    /// <summary>The items removed since the last commit.</summary>
    public IReadOnlyList<T> Removed { get; }
    /// <summary>Accepts the removals, clearing the tracked removed items.</summary>
    public void CommitRemoved();
    /// <summary>The original value at an index, if one is tracked there.</summary>
    bool HasOriginal(int index, [MaybeNullWhen(false)] out T original);
}
/// <summary>A list that tracks which items are being edited, with commit and cancel per item.</summary>
public interface ITrackingEditList : ICount {
    /// <summary>Gets whether any item has an active edit.</summary>
    public bool HasChanges { get; }
    /// <summary>Gets whether the item at the index is being edited.</summary>
    public bool IsEditing(int index);
    /// <summary>Starts editing the item when it is not already being edited.</summary>
    public bool EnsureEditing(int index);
    /// <summary>Commits the edit at the given index.</summary>
    public bool CommitEdit(int index);
    /// <summary>Cancels the edit at the given index.</summary>
    public bool CancelEdit(int index, bool canRemove = false);
    /// <summary>Commits the edit at the given index.</summary>
    public Task<bool> CommitEditAsync(int index);
    /// <summary>Cancels the edit at the given index.</summary>
    public Task<bool> CancelEditAsync(int index);
}
/// <summary>The typed edit list, handing back the editable value for the item at an index.</summary>
public interface ITrackingEditList<T> : IList<T>, ITrackingEditList {
    int ICount.ItemCount => Count;
    /// <summary>The pending edit value at an index, if the item is being edited.</summary>
    public bool HasEditValue(int index, [MaybeNullWhen(false)] out T editValue);
    /// <summary>Puts the item at an index into editing and hands back its editable value.</summary>
    public bool EnsureEditing(int index, [MaybeNullWhen(false)] out T editValue);
}
/// <summary>A list that carries a piece of metadata per item, such as a validation error.</summary>
public interface IMetadataList<out TMetadata> : ICount {
    /// <summary>The metadata for the item at an index.</summary>
    public TMetadata? GetMetadata(int index);
}
/// <summary>A list that can validate its items and report whether each is currently valid.</summary>
public interface IValidatableList : ICount {
    /// <summary>Runs validation on the item at an index, recording any error.</summary>
    public bool Validate(int index);
    /// <summary>Whether the item at an index is currently valid.</summary>
    public bool IsValid(int index);
}
/// <summary>A validatable list whose validation errors are of type <typeparamref name="TError"/>.</summary>
public interface IValidatableList<out TError> : IValidatableList, IMetadataList<TError>;
/// <summary>Combines original value tracking with editable items.</summary>
public interface IEditableList<TOg> : ITrackingList<TOg>, ITrackingEditList;
/// <summary>Combines typed original value tracking with typed editable items.</summary>
public interface IEditableList<TOg, TEdit> : IEditableList<TOg>, ITrackingEditList<TEdit>;
/// <summary>Combines editable items with one metadata value per item.</summary>
public interface IMetadataEditableList<TEdit, TMetadata> : ITrackingEditList<TEdit>, IMetadataList<TMetadata>;
/// <summary>Combines editable items with validation errors.</summary>
public interface IValidatableEditableList<TError> : ITrackingEditList, IValidatableList<TError>;
/// <summary>Combines typed editable items with validation errors.</summary>
public interface IValidatableEditableList<TEdit, TError> : ITrackingEditList<TEdit>, IValidatableEditableList<TError>;
/// <summary>Combines original value tracking, typed edits, and validation errors.</summary>
public interface IValidatableEditableList<TOg, TEdit, TError> : IEditableList<TOg, TEdit>, IValidatableEditableList<TEdit, TError>;
