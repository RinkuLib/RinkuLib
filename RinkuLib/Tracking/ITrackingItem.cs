using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;
/// <summary>
/// An item that tracks an original value
/// </summary>
public interface ITrackingItem<T> {
    /// <summary>
    /// Attempts to reattach the specified value to this item.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the value match and was claimed
    /// </returns>
    bool TryReattach(T value);
    /// <summary>
    /// Attempts to retrieve the original tracked value.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if an original value is tracked
    /// </returns>
    bool HasOriginal([MaybeNullWhen(false)] out T original);
}
/// <summary>
/// An item that exposes a value and optionally supports editing it through
/// a dedicated editable representation.
/// </summary>
public interface IEditableItem<T> {
    /// <summary>
    /// Gets the value currently represented by the item.
    /// </summary>
    /// <remarks>
    /// No guarantees are made regarding the mutability of the returned value.
    /// Callers that intend to modify the value should use <see cref="EditableValue"/>.
    /// </remarks>
    T? CurrentValue { get; }
    /// <summary>
    /// Gets a value that can be modified.
    /// </summary>
    T? EditableValue { get; }
    /// <summary>
    /// Makes sure that the current state of the item is editing.
    /// </summary>
    bool EnsureIsEditing([MaybeNullWhen(false)] out T editableValue);
    /// <summary>
    /// Gets a value indicating whether the item is currently being edited.
    /// </summary>
    bool IsEditing { get; }
    /// <summary>
    /// Gets a value indicating whether the item contains changes.
    /// </summary>
    bool HasChanges { get; }
    /// <summary>
    /// Applies any pending edits.
    /// </summary>
    bool CommitEdit();
    /// <summary>
    /// Discards any pending edits.
    /// </summary>
    bool CancelEdit();
    /// <summary>
    /// Applies any pending edits.
    /// </summary>
    Task<bool> CommitEditAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Discards any pending edits.
    /// </summary>
    Task<bool> CancelEditAsync(CancellationToken cancellationToken = default);
}
/// <summary>
/// An item that exposes a value for editing while also tracking an original value
/// </summary>
/// <typeparam name="TOg">
/// The type of the original tracked value.
/// </typeparam>
/// <typeparam name="TEdit">
/// The type of the editable representation of the value.
/// </typeparam>
public interface IEditableItem<TOg, TEdit> : IEditableItem<TEdit>, ITrackingItem<TOg>;
/// <summary>
/// Exposes the ability to be built from an original value
/// </summary>
public interface IEditableItemFromOriginal<TOg, TSelf> where TSelf : IEditableItemFromOriginal<TOg, TSelf> {
    /// <summary>
    /// Creates a tracking item from an original value.
    /// </summary>
    public abstract static TSelf FromOriginal(TOg original);
}
/// <summary>
/// Exposes the ability to be built from an edit value
/// </summary>
public interface IEditableItemFromEdit<TEdit, TSelf> where TSelf : IEditableItemFromEdit<TEdit, TSelf> {
    /// <summary>
    /// Creates a tracking item initialized directly with an editable value.
    /// </summary>
    public abstract static TSelf CreateNew(TEdit value);
}
/// <summary>
/// Exposes the ability to be built from an original and an edit value
/// </summary>
public interface IEditableItemFrom<TOg, TEdit, TSelf> : IEditableItemFromOriginal<TOg, TSelf>, IEditableItemFromEdit<TEdit, TSelf> where TSelf : IEditableItemFrom<TOg, TEdit, TSelf>;
/// <summary>
/// Represents metadata that can validate a value and expose an associated error when invalid.
/// </summary>
/// <typeparam name="TMetadata">The type of validation error produced when invalid.</typeparam>
public interface IMetadata<out TMetadata> {
    /// <summary>
    /// Gets the validation error associated with the metadata, if any.
    /// </summary>
    TMetadata? Metadata { get; }
}
/// <summary>
/// Represents metadata that can validate a value and expose an associated error when invalid.
/// </summary>
/// <typeparam name="TMetadata">The type of validation error produced when invalid.</typeparam>
public interface IMetadataSetter<TMetadata> {
    /// <summary>
    /// Sets the validation error associated with the metadata, if any.
    /// </summary>
    void SetMetadata(TMetadata? metadata);
}