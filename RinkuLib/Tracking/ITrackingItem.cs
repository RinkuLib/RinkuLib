using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;
/// <summary>
/// Represents an item that tracks an original value
/// </summary>
public interface ITrackingItem<T>
{
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
/// Represents an item that exposes a value and optionally supports editing it through
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
    /// <remarks>
    /// Accessing this property may change the state of tracking by entering an edit mode
    /// </remarks>
    T EditableValue { get; }
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
    void CommitEdit();
    /// <summary>
    /// Discards any pending edits.
    /// </summary>
    bool CancelEdit();
    /// <summary>
    /// Applies any pending edits.
    /// </summary>
    Task CommitEditAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Discards any pending edits.
    /// </summary>
    Task<bool> CancelEditAsync(CancellationToken cancellationToken = default);
}
/// <summary>
/// Represents an item that exposes a value for editing while also tracking an original value
/// </summary>
/// <typeparam name="TOg">
/// The type of the original tracked value.
/// </typeparam>
/// <typeparam name="TEdit">
/// The type of the editable representation of the value.
/// </typeparam>
public interface IEditableItem<TOg, TEdit> : IEditableItem<TEdit>, ITrackingItem<TOg>;