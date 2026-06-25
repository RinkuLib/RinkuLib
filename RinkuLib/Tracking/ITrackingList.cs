using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;
/// <summary>Contains a count</summary>
public interface ICount {
    /// <summary>Get the number of elements in the collection</summary>
    public int ItemCount { get; }
}
/// <summary>A list track original values removal and presence state</summary>
public interface ITrackingList<T> : ICount {
    /// <summary>The collection of removed items</summary>
    public IReadOnlyList<T> Removed { get; }
    /// <summary>Will discard the tracked removed items</summary>
    public void CommitRemoved();
    /// <summary>Check if there is an original value at a given index</summary>
    bool HasOriginal(int index, [MaybeNullWhen(false)] out T original);
}
/// <summary>A list that track the edit state of items</summary>
public interface ITrackingEditList : ICount {
    /// <summary>Indicate if an element in the collection contains an edit</summary>
    public bool HasChanges { get; }
    /// <summary>Indicate if the element at an index is editing</summary>
    public bool IsEditing(int index);
    /// <summary>Ensure that the item is in an editing state</summary>
    public bool EnsureEditing(int index);
    /// <summary>Commit the edit of the element at the index</summary>
    public bool CommitEdit(int index);
    /// <summary>Cancel the edit of the element at the index</summary>
    public bool CancelEdit(int index, bool canRemove = false);
    /// <summary>Commit the edit of the element at the index</summary>
    public Task<bool> CommitEditAsync(int index);
    /// <summary>Cancel the edit of the element at the index</summary>
    public Task<bool> CancelEditAsync(int index);
}
/// <summary>A list that track the edit state of items</summary>
public interface ITrackingEditList<T> : IList<T>, ITrackingEditList {
    int ICount.ItemCount => Count;
    /// <summary>Check if there is an edit value at a given index</summary>
    public bool HasEditValue(int index, [MaybeNullWhen(false)] out T editValue);
    /// <summary>Ensure that the item is in an editing state</summary>
    public bool EnsureEditing(int index, [MaybeNullWhen(false)] out T editValue);
}
/// <summary>A list that track metadata of items</summary>
public interface IMetadataList<out TMetadata> : ICount {
    /// <summary>Get the metadata of the item at the specified index</summary>
    public TMetadata? GetMetadata(int index);
}
/// <summary>A can provide errors metadata and validation</summary>
public interface IValidatableList : ICount {
    /// <summary>Validate the item at the specified index</summary>
    public bool Validate(int index);
    /// <summary>Indicate if the current state of the item at the specified index is valid</summary>
    public bool IsValid(int index);
}
/// <summary>A can provide errors metadata and validation</summary>
public interface IValidatableList<out TError> : IValidatableList, IMetadataList<TError>;
/// <summary>A combinaison of <see cref="ITrackingList{TOg}"/> and <see cref="ITrackingEditList"/></summary>
public interface IEditableList<TOg> : ITrackingList<TOg>, ITrackingEditList;
/// <summary>A combinaison of <see cref="ITrackingList{TOg}"/> and <see cref="ITrackingEditList{TEdit}"/></summary>
public interface IEditableList<TOg, TEdit> : IEditableList<TOg>, ITrackingEditList<TEdit>;
/// <summary>A combinaison of <see cref="ITrackingList{TOg}"/>, <see cref="ITrackingEditList{TEdit}"/> and  <see cref="IMetadataList{TMetadata}"/></summary>
public interface IMetadataEditableList<TEdit, TMetadata> : ITrackingEditList<TEdit>, IMetadataList<TMetadata>;
/// <summary>A combinaison of <see cref="ITrackingEditList"/> and <see cref="IValidatableList{TError}"/></summary>
public interface IValidatableEditableList<TError> : ITrackingEditList, IValidatableList<TError>;
/// <summary>A combinaison of <see cref="ITrackingEditList{TEdit}"/> and <see cref="IValidatableList{TError}"/></summary>
public interface IValidatableEditableList<TEdit, TError> : ITrackingEditList<TEdit>, IValidatableEditableList<TError>;
/// <summary>A combinaison of <see cref="ITrackingList{TOg}"/>, <see cref="ITrackingEditList{TEdit}"/> and <see cref="IValidatableList{TError}"/></summary>
public interface IValidatableEditableList<TOg, TEdit, TError> : IEditableList<TOg, TEdit>, IValidatableEditableList<TEdit, TError>;
