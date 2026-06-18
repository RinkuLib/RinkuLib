using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace RinkuLib.Tracking;
/// <summary>
/// Represents a list of items that support editing and tracking of original values. (expose <typeparamref name="TEdit"/>)
/// </summary>
public class TrackingEditList<TOg, TEdit, TEditItem>(IEnumerable<TOg> items, int initialCapacity = 4)
    : TrackingEditListBase<TOg, TEdit, TEditItem>(items.Select(TEditItem.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity)
    where TEditItem : IEditableItem<TEdit>, ITrackingItem<TOg>, IEditableItemFromOriginal<TOg, TEditItem>, IEditableItemFromEdit<TEdit, TEditItem> {
    /// <inheritdoc/>
    public override TEditItem MakeNewEditItem(TEdit newItem) => TEditItem.CreateNew(newItem);
}
/// <summary>
/// Represents a list of items that support editing and tracking of original values. (expose <typeparamref name="T"/>)
/// </summary>
public class TrackingEditList<T, TEditItem>(IEnumerable<T> items, int initialCapacity = 4)
    : TrackingEditListBase<T, T, TEditItem>(items.Select(TEditItem.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity)
    where TEditItem : IEditableItem<T>, ITrackingItem<T>, IEditableItemFromOriginal<T, TEditItem>, IEditableItemFromEdit<T, TEditItem> {
    /// <inheritdoc/>
    public override TEditItem MakeNewEditItem(T newItem) => TEditItem.CreateNew(newItem);
}
/// <summary>
/// Represents a list of reference-type items that support editing and tracking of original values.
/// </summary>
public class TrackingEditListt<T>(IEnumerable<T> items, int initialCapacity = 4)
    : TrackingEditListBase<T, T, EditableClass<T>>(items.Select(EditableClass<T>.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity)
    where T : class {
    /// <inheritdoc/>
    public override EditableClass<T> MakeNewEditItem(T newItem) => EditableClass<T>.CreateNew(newItem);
}
/// <summary>
/// Represents a list of value-type items that support editing and tracking of original values.
/// </summary>
public class TrackingEditStructList<T>(IEnumerable<T> items, int initialCapacity = 4)
    : TrackingEditListBase<T, T, EditableStruct<T>>(items.Select(EditableStruct<T>.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity)
    where T : struct {
    /// <inheritdoc/>
    public override EditableStruct<T> MakeNewEditItem(T newItem) => EditableStruct<T>.CreateNew(newItem);
}
/// <summary>
/// Represents a list of reference-type items with attached metadata that support editing and tracking of original values.
/// </summary>
public class TrackingEditListt<T, TMetadata>(IEnumerable<T> items, int initialCapacity = 4)
    : TrackingEditListBase<T, T, EditableClass<T, TMetadata>>(items.Select(EditableClass<T, TMetadata>.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity), IEditableList<T, T, TMetadata>
    where T : class {
    /// <inheritdoc/>
    public override EditableClass<T, TMetadata> MakeNewEditItem(T newItem) => EditableClass<T, TMetadata>.CreateNew(newItem);
    /// <inheritdoc/>
    public virtual void SetMetadata(int index, TMetadata metadata) {
        ref var trackedItem = ref Get(index);
        trackedItem.Metadata = metadata;
    }
    /// <inheritdoc/>
    public virtual ref TMetadata GetMetadata(int index) {
        ref var trackedItem = ref Get(index);
        return ref trackedItem.Metadata;
    }
}
/// <summary>
/// Represents a list of value-type items with attached metadata that support editing and tracking of original values.
/// </summary>
public class TrackingEditStructList<T, TMetadata>(IEnumerable<T> items, int initialCapacity = 4)
    : TrackingEditListBase<T, T, EditableStruct<T, TMetadata>>(items.Select(EditableStruct<T, TMetadata>.FromOriginal),
        items.TryGetNonEnumeratedCount(out var count) && initialCapacity < count ? count : initialCapacity), IEditableList<T, T, TMetadata>
    where T : struct {
    /// <inheritdoc/>
    public override EditableStruct<T, TMetadata> MakeNewEditItem(T newItem) => EditableStruct<T, TMetadata>.CreateNew(newItem);
    /// <inheritdoc/>
    public virtual void SetMetadata(int index, TMetadata metadata) {
        ref var trackedItem = ref Get(index);
        trackedItem.Metadata = metadata;
    }
    /// <inheritdoc/>
    public virtual ref TMetadata GetMetadata(int index) {
        ref var trackedItem = ref Get(index);
        return ref trackedItem.Metadata;
    }
}