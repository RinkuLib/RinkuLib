using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;

/// <summary>
/// Represents a reference-type editable item that tracks an original value and supports
/// a lazy editable copy of that value.
/// </summary>
public struct EditableClass<T> : IEditableItem<T, T> where T : class {
    private T? _editValue;
    private T? _original;
    /// <summary>
    /// Creates a tracking item from an original value.
    /// </summary>
    public static EditableClass<T> FromOriginal(T original) => new() { _original = original };
    /// <summary>
    /// Creates a tracking item initialized directly with an editable value.
    /// </summary>
    public static EditableClass<T> CreateNew(T current) => new() { _editValue = current };
    /// <inheritdoc/>
    public bool TryReattach(T original) {
        if (!EqualityComparer<T>.Default.Equals(original, _original ?? _editValue))
            return false;
        _original = original;
        return true;
    }
    /// <inheritdoc/>
    public readonly bool HasOriginal([MaybeNullWhen(false)] out T original) {
        original = _original;
        return original is not null;
    }
    /// <inheritdoc/>
    public readonly bool IsEditing => _editValue is not null;
    /// <inheritdoc/>
    public readonly bool HasChanges => IsEditing;
    /// <inheritdoc/>
    public readonly T? CurrentValue => _editValue ?? _original;
    /// <inheritdoc/>
    public T EditableValue => _editValue ??= Copier<T>.Copy(_original ?? throw new Exception("Original was not present"));
    /// <inheritdoc/>
    public void CommitEdit() {
        _original = _editValue;
        _editValue = null;
    }
    /// <inheritdoc/>
    public bool CancelEdit() {
        if (_editValue is null || _original is null)
            return false;
        _editValue = null;
        return true;
    }
    /// <inheritdoc/>
    public Task CommitEditAsync(CancellationToken cancellationToken = default) {
        CommitEdit();
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public Task<bool> CancelEditAsync(CancellationToken cancellationToken = default) 
        => Task.FromResult(CancelEdit());
}
/// <summary>
/// Represents a value-type editable item that tracks an original value and supports
/// a lazy editable copy of that value.
/// </summary>
public struct EditableStruct<T> : IEditableItem<T, T> where T : struct {
    private T _editValue;
    private T _original;
    private bool _isEditing;
    private bool _hasOriginal;
    /// <summary>
    /// Creates a tracking item from an original value.
    /// </summary>
    public static EditableStruct<T> FromOriginal(T original) => new() { _original = original, _hasOriginal = true };
    /// <summary>
    /// Creates a tracking item initialized directly with an editable value.
    /// </summary>
    public static EditableStruct<T> CreateNew(T current) => new() { _editValue = current, _isEditing = true };
    /// <inheritdoc/>
    public bool TryReattach(T original) {
        if (!EqualityComparer<T>.Default.Equals(original, _hasOriginal ? _original : _isEditing ? _editValue : default))
            return false;
        _original = original;
        return true;
    }
    /// <inheritdoc/>
    public readonly bool HasOriginal([MaybeNullWhen(false)] out T original) {
        original = _original;
        return _hasOriginal;
    }
    /// <inheritdoc/>
    public readonly bool IsEditing => _isEditing;
    /// <inheritdoc/>
    public readonly bool HasChanges => IsEditing;
    /// <inheritdoc/>
    public readonly T OriginalValue => _original;
    /// <inheritdoc/>
    public readonly T CurrentValue => _isEditing ? _editValue : _hasOriginal ? _original : throw new Exception("Original was not present");
    /// <inheritdoc/>
    public T EditableValue {
        get {
            if (_isEditing)
                return _editValue;
            if (!_hasOriginal)
                throw new Exception("Original was not present");
            _editValue = Copier<T>.Copy(_original);
            _isEditing = true;
            return _editValue;
        }
    }
    /// <inheritdoc/>
    public void CommitEdit() {
        if (!_isEditing)
            return;
        _original = _editValue;
        _editValue = default;
        _hasOriginal = true;
        _isEditing = false;
    }
    /// <inheritdoc/>
    public bool CancelEdit() {
        if (!_isEditing || !_hasOriginal)
            return false;
        _editValue = default;
        _isEditing = false;
        return true;
    }
    /// <inheritdoc/>
    public Task CommitEditAsync(CancellationToken cancellationToken = default) {
        CommitEdit();
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public Task<bool> CancelEditAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CancelEdit());
}
/// <summary>
/// Represents a reference-type editable item with attached metadata.
/// </summary>
/// <typeparam name="T">The reference type being tracked.</typeparam>
/// <typeparam name="TMetadata">Additional metadata associated with the item.</typeparam>
public struct EditableClass<T, TMetadata> : IEditableItem<T, T> where T : class {
    /// <summary>
    /// Metadata associated with this item.
    /// </summary>
    public TMetadata Metadata;
    private EditableClass<T> _editableItem;
    /// <summary>
    /// Creates a tracking item from an original value.
    /// </summary>
    public static EditableClass<T, TMetadata> FromOriginal(T original) => new() { _editableItem = EditableClass<T>.FromOriginal(original) };
    /// <summary>
    /// Creates a tracking item initialized directly with an editable value.
    /// </summary>
    public static EditableClass<T, TMetadata> CreateNew(T current) => new() { _editableItem = EditableClass<T>.CreateNew(current) };
    /// <inheritdoc/>
    public T EditableValue => _editableItem.EditableValue;
    /// <inheritdoc/>
    public readonly bool IsEditing => _editableItem.IsEditing;
    /// <inheritdoc/>
    public readonly T? CurrentValue => _editableItem.CurrentValue;
    /// <inheritdoc/>
    public readonly bool HasChanges => _editableItem.HasChanges;
    /// <inheritdoc/>
    public bool TryReattach(T value) => _editableItem.TryReattach(value);
    /// <inheritdoc/>
    public readonly bool HasOriginal([MaybeNullWhen(false)] out T original) => _editableItem.HasOriginal(out original);
    /// <inheritdoc/>
    public void CommitEdit() => _editableItem.CommitEdit();
    /// <inheritdoc/>
    public bool CancelEdit() => _editableItem.CancelEdit();
    /// <inheritdoc/>
    public Task CommitEditAsync(CancellationToken cancellationToken = default) => _editableItem.CommitEditAsync(cancellationToken);
    /// <inheritdoc/>
    public Task<bool> CancelEditAsync(CancellationToken cancellationToken = default) => _editableItem.CancelEditAsync(cancellationToken);
}
/// <summary>
/// Represents a value-type editable item with attached metadata.
/// </summary>
/// <typeparam name="T">The value type being tracked.</typeparam>
/// <typeparam name="TMetadata">Additional metadata associated with the item.</typeparam>
public struct EditableStruct<T, TMetadata> : IEditableItem<T, T> where T : struct {
    /// <summary>
    /// Metadata associated with this item.
    /// </summary>
    public TMetadata Metadata;
    private EditableStruct<T> _editableItem;
    /// <summary>
    /// Creates a tracking item from an original value.
    /// </summary>
    public static EditableStruct<T, TMetadata> FromOriginal(T original) => new() { _editableItem = EditableStruct<T>.FromOriginal(original) };
    /// <summary>
    /// Creates a tracking item initialized directly with an editable value.
    /// </summary>
    public static EditableStruct<T, TMetadata> CreateNew(T current) => new() { _editableItem = EditableStruct<T>.CreateNew(current) };
    /// <inheritdoc/>
    public T EditableValue => _editableItem.EditableValue;
    /// <inheritdoc/>
    public readonly bool IsEditing => _editableItem.IsEditing;
    /// <inheritdoc/>
    public readonly T CurrentValue => _editableItem.CurrentValue;
    /// <inheritdoc/>
    public readonly bool HasChanges => _editableItem.HasChanges;
    /// <inheritdoc/>
    public bool TryReattach(T value) => _editableItem.TryReattach(value);
    /// <inheritdoc/>
    public readonly bool HasOriginal([MaybeNullWhen(false)] out T original) => _editableItem.HasOriginal(out original);
    /// <inheritdoc/>
    public void CommitEdit() => _editableItem.CommitEdit();
    /// <inheritdoc/>
    public bool CancelEdit() => _editableItem.CancelEdit();
    /// <inheritdoc/>
    public Task CommitEditAsync(CancellationToken cancellationToken = default) => _editableItem.CommitEditAsync(cancellationToken);
    /// <inheritdoc/>
    public Task<bool> CancelEditAsync(CancellationToken cancellationToken = default) => _editableItem.CancelEditAsync(cancellationToken);
}