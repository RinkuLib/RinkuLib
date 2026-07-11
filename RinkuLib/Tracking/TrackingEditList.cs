namespace RinkuLib.Tracking;

/// <summary>
/// A processor that validates and commits edit operations.
/// </summary>
/// <typeparam name="TEdit">The type of editable value being processed.</typeparam>
/// <typeparam name="TMetadata">
/// The type of metadata produced by validation and commit operations.
/// Metadata may contain validation errors, warnings, commit results, or other contextual information.
/// </typeparam>
public interface IEditProcessor<TEdit, TMetadata> {
    /// <summary>
    /// Gets a value indicating whether this processor supports commit operations.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, <see cref="Commit(TEdit)"/> should not be called.
    /// </remarks>
    bool DoCommit { get; }

    /// <summary>
    /// Gets a value indicating whether this processor supports validation operations.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, <see cref="Validate(TEdit, object)"/> should not be called.
    /// </remarks>
    bool DoValidate { get; }

    /// <summary>
    /// Validates the specified value and returns metadata describing the validation result.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="context">
    /// Optional contextual data used during validation.
    /// The meaning of this parameter is implementation-specific.
    /// </param>
    /// <returns>
    /// Metadata describing the validation result
    /// </returns>
    TMetadata? Validate(TEdit? value, object? context);

    /// <summary>
    /// Commits the specified value and returns metadata describing the commit result.
    /// </summary>
    /// <param name="value">The value to commit.</param>
    /// <returns>
    /// Metadata describing the commit result
    /// </returns>
    TMetadata? Commit(TEdit value);

    /// <summary>
    /// Determines whether the specified metadata represents a valid result.
    /// </summary>
    /// <param name="metadata">The metadata to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> if the metadata represents a valid result, otherwise <see langword="false"/>.
    /// </returns>
    bool IsValid(TMetadata? metadata);
}
/// <summary>
/// A list of items that support editing and tracking of original values. (expose <typeparamref name="TEdit"/>)
/// </summary>
public class TrackingEditList<TOg, TEdit, TEditItem>(IEnumerable<TOg> items, int initialCapacity = 4)
    : TrackingEditListBase<TOg, TEdit, TEditItem>(items.Select(TEditItem.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity)
    where TEditItem : IEditableItem<TEdit>, ITrackingItem<TOg>, IEditableItemFromOriginal<TOg, TEditItem>, IEditableItemFromEdit<TEdit, TEditItem> {
    /// <inheritdoc/>
    public override TEditItem MakeNewEditItem(TEdit newItem) => TEditItem.CreateNew(newItem);
}
/// <summary>
/// A list of items that support editing and tracking of original values. (expose <typeparamref name="TEdit"/>)
/// </summary>
public class TrackingEditList<TOg, TEdit, TEditItem, TMetadata, TEditProcessor>(TEditProcessor processor, IEnumerable<TOg> items, int initialCapacity = 4)
    : TrackingEditListBase<TOg, TEdit, TEditItem>(items.Select(TEditItem.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity), IValidatableEditableList<TOg, TEdit, TMetadata>, IMetadataEditableList<TEdit, TMetadata>
    where TEditItem : IEditableItem<TEdit>, ITrackingItem<TOg>, IEditableItemFromOriginal<TOg, TEditItem>, IEditableItemFromEdit<TEdit, TEditItem>, IMetadata<TMetadata>, IMetadataSetter<TMetadata>
    where TEditProcessor : IEditProcessor<TEdit, TMetadata> {
    /// <inheritdoc/>
    public override TEditItem MakeNewEditItem(TEdit newItem) => TEditItem.CreateNew(newItem);
    private readonly TEditProcessor _processor = processor;
    /// <inheritdoc/>
    public TMetadata? GetMetadata(int index) => Get(index).Metadata;
    ///<inheritdoc/>
    public override bool CommitEdit(int index) {
        if (!_processor.DoCommit)
            return base.CommitEdit(index);
        ref var item = ref Get(index);
        if (!item.IsEditing)
            return true;
        if (item.EditableValue is null)
            return false;
        var meta = _processor.Commit(item.EditableValue);
        item.SetMetadata(meta);
        if (!_processor.IsValid(meta))
            return false;
        return item.CommitEdit();
    }
    /// <inheritdoc/>
    public bool IsValid(int index) => _processor.IsValid(Get(index).Metadata);
    /// <inheritdoc/>
    public bool Validate(int index) {
        if (!_processor.DoValidate)
            return true;
        ref var item = ref Get(index);
        item.SetMetadata(_processor.Validate(item.CurrentValue, this));
        return _processor.IsValid(item.Metadata);
    }
}