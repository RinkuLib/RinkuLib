using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Rinku.Tracking.Runtime;

internal interface IRuntimeTrackingListMaterializationContext<in TEdit>
{
    void TrackInitial(TEdit item, int sourceIndex);
}

/// <summary>Default context for generated edits when there is no mutable/indexable external collection to synchronize.</summary>
public sealed class RuntimeGeneratedTrackingListContext<TOriginal, TEdit> : ITrackingListContext<TEdit>
{
    private readonly RuntimeTrackingRegistration<TOriginal, TEdit> _registration;

    /// <summary>Creates a generated item context.</summary>
    public RuntimeGeneratedTrackingListContext(RuntimeTrackingRegistration<TOriginal, TEdit> registration)
        => _registration = registration ?? throw new ArgumentNullException(nameof(registration));

    /// <inheritdoc/>
    public bool CanCreateNew => _registration.CanCreateNew;
    /// <inheritdoc/>
    public TEdit CreateNew() => _registration.CreateNew();

    /// <inheritdoc/>
    public bool ConfirmAdded(TEdit item)
    {
        if (item is IEditable editable && !editable.ConfirmEdit()) return false;
        return item is not IRuntimeNewStateControl state || state.ConfirmNew();
    }

    /// <inheritdoc/>
    public bool ConfirmEdit(TEdit item)
        => item is not IEditable editable || editable.ConfirmEdit();

    /// <inheritdoc/>
    public bool ConfirmDelete(TEdit item) => true;
}

/// <summary>
/// Source-aware generated context for indexable originals. Source slot identity is tracked independently from equality,
/// so an accepted replacement is written back even when it compares equal to the previous original.
/// </summary>
public sealed class RuntimeIndexedTrackingListContext<TOriginal, TEdit> : ITrackingListContext<TEdit>, IRuntimeTrackingListMaterializationContext<TEdit>
{
    private readonly IList<TOriginal> _source;
    private readonly BindingList<TOriginal>? _bindingSource;
    private readonly bool _canWriteIndex;
    private readonly RuntimeTrackingRegistration<TOriginal, TEdit> _registration;
    private readonly Dictionary<object, int> _sourceIndexes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates a context for an indexable source.</summary>
    public RuntimeIndexedTrackingListContext(IList<TOriginal> source, RuntimeTrackingRegistration<TOriginal, TEdit> registration)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bindingSource = source as BindingList<TOriginal>;
        _canWriteIndex = source is TOriginal[] || !source.IsReadOnly;
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc/>
    public bool CanCreateNew => _registration.CanCreateNew;
    /// <inheritdoc/>
    public TEdit CreateNew() => _registration.CreateNew();

    /// <inheritdoc/>
    public bool ConfirmAdded(TEdit item)
    {
        if (_source.IsReadOnly) return false;
        if (item is IEditable editable && !editable.ConfirmEdit()) return false;
        if (!TryOriginal(item, out TOriginal? original)) return false;

        if (!TryGetSourceIndex(item, out _))
        {
            int index = _source.Count;
            _source.Add(original);
            Track(item, index);
        }

        return item is not IRuntimeNewStateControl state || state.ConfirmNew();
    }

    /// <inheritdoc/>
    public bool ConfirmEdit(TEdit item)
    {
        if (!TryGetSourceIndex(item, out int index))
        {
            if (item is not ITrackingListNewState { IsNew: true }) return false;
            return item is not IEditable pending || pending.ConfirmEdit();
        }

        bool needsSourceRefresh = true;
        if (item is IEditable editable)
        {
            needsSourceRefresh = editable.IsEditing;
            if (!editable.ConfirmEdit()) return false;
        }

        if (!needsSourceRefresh) return true;
        if (!TryOriginal(item, out TOriginal? accepted)) return false;

        if (typeof(TOriginal).IsValueType)
        {
            if (!_canWriteIndex) return false;
            _source[index] = accepted;
            return true;
        }

        TOriginal current = _source[index];
        if (!ReferenceEquals(current, accepted))
        {
            // Reference identity, not equality, decides replacement. Equal-but-different instances must still be adopted.
            if (!_canWriteIndex) return false;
            _source[index] = accepted;
            return true;
        }

        if (accepted is INotifyPropertyChanged)
            return true;

        if (_bindingSource is not null)
            _bindingSource.ResetItem(index);
        else if (_canWriteIndex)
            // Generic IList has no notification contract; assigning the same instance is the broadest available index refresh.
            _source[index] = accepted;

        return true;
    }

    /// <inheritdoc/>
    public bool ConfirmDelete(TEdit item)
    {
        if (_source.IsReadOnly || !TryGetSourceIndex(item, out int index)) return false;

        _source.RemoveAt(index);
        Untrack(item);
        ShiftAfterRemoval(index);
        return true;
    }

    void IRuntimeTrackingListMaterializationContext<TEdit>.TrackInitial(TEdit item, int sourceIndex)
        => Track(item, sourceIndex);

    private static bool TryOriginal(TEdit item, [MaybeNullWhen(false)] out TOriginal original)
    {
        if (item is IOriginal<TOriginal> source && source.TryGetOriginal(out original)) return true;
        original = default;
        return false;
    }

    private void Track(TEdit item, int sourceIndex)
    {
        object key = (object?)item
            ?? throw new InvalidOperationException($"Runtime-generated tracking item {typeof(TEdit)} must be a reference type.");
        _sourceIndexes[key] = sourceIndex;
    }

    private void Untrack(TEdit item)
    {
        object key = (object?)item
            ?? throw new InvalidOperationException($"Runtime-generated tracking item {typeof(TEdit)} must be a reference type.");
        _sourceIndexes.Remove(key);
    }

    private bool TryGetSourceIndex(TEdit item, out int index)
    {
        object? key = (object?)item;
        if (key is not null && _sourceIndexes.TryGetValue(key, out index) && (uint)index < (uint)_source.Count)
            return true;
        index = -1;
        return false;
    }

    private void ShiftAfterRemoval(int removedIndex)
    {
        if (_sourceIndexes.Count == 0) return;

        object[]? shifted = null;
        int shiftedCount = 0;
        foreach (KeyValuePair<object, int> pair in _sourceIndexes)
        {
            if (pair.Value <= removedIndex) continue;
            shifted ??= new object[_sourceIndexes.Count];
            shifted[shiftedCount++] = pair.Key;
        }

        if (shifted is null) return;
        for (int i = 0; i < shiftedCount; i++)
            _sourceIndexes[shifted[i]]--;
    }
}
