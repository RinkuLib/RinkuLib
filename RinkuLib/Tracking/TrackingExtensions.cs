using System.Linq.Expressions;

namespace RinkuLib.Tracking;
internal static class DefaultFactoryCache<T> where T : class {
    public static readonly Func<T>? Factory;

    static DefaultFactoryCache() {
        var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
        if (ctor != null) {
            var newExpr = Expression.New(ctor);
            Factory = Expression.Lambda<Func<T>>(newExpr).Compile();
        }
    }
}
/// <summary>Wraps a sequence of reference-type items in a change-tracking list, <c>ToTrackingList()</c>, with optional validation and commit handling.</summary>
public static class TrackingExtensions {
    /// <summary>
    /// Wraps a sequence in a tracking list driven by a custom edit processor.
    /// </summary>
    public static TrackingEditList<T, T, EditableClass<T, TMetadata>, TMetadata, TEditProcessor> ToTrackingList<T, TMetadata, TEditProcessor>(this IEnumerable<T> source, TEditProcessor editProcessor, Func<T>? newItem = null, int initialCapacity = 4)
        where T : class where TEditProcessor : IEditProcessor<T, TMetadata> => source.ToTrackingList<T, TMetadata, EditableClass<T, TMetadata>, TEditProcessor>(editProcessor, newItem ?? DefaultFactoryCache<T>.Factory, initialCapacity);
    /// <summary>
    /// Creates a TrackingEditList for reference types (classes).
    /// </summary>
    public static TrackingEditList<T, T, TEditItem, TMetadata, TEditProcessor> ToTrackingList<T, TMetadata, TEditItem, TEditProcessor>(this IEnumerable<T> source, TEditProcessor editProcessor, Func<T>? newItem = null, int initialCapacity = 4)
        where TEditItem : IEditableItem<T>, ITrackingItem<T>, IEditableItemFromOriginal<T, TEditItem>, IEditableItemFromEdit<T, TEditItem>, IMetadata<TMetadata>, IMetadataSetter<TMetadata>
        where TEditProcessor : IEditProcessor<T, TMetadata> {
        var list = new TrackingEditList<T, T, TEditItem, TMetadata, TEditProcessor>(editProcessor, source, initialCapacity);
        if (newItem is not null)
            list.SetNewItemFactory(newItem);
        return list;
    }
    /// <summary>
    /// Creates a TrackingEditList for reference types (classes).
    /// </summary>
    public static TrackingEditList<T, T, EditableClass<T>> ToTrackingList<T>(this IEnumerable<T> source, Func<T>? newItem = null, int initialCapacity = 4) where T : class {
        var res = new TrackingEditList<T, T, EditableClass<T>>(source, initialCapacity);
        newItem ??= DefaultFactoryCache<T>.Factory;
        if (newItem is not null)
            res.SetNewItemFactory(newItem);
        return res;
    }
    /// <summary>
    /// Creates a TrackingEditList with metadata support for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableClass<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<TMetadata?, bool>? isValid = null, Func<T>? newItem = null, int initialCapacity = 4) where T : class
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, NoOpEditProcessor<T, TMetadata>>(new(), newItem, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(null, null, isValid), newItem, initialCapacity);

    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Validator for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableClass<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T?, object?, TMetadata?> validator, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : class
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(validator, null), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(validator, null, isValid), null, initialCapacity);
    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Commit for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableClass<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T?, TMetadata?> committer, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : class
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(null, committer), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(null, committer, isValid), null, initialCapacity);
    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Commit and Validation for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableClass<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T?, object?, TMetadata?> validator, Func<T?, TMetadata?> committer, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : class
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(validator, committer), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(validator, committer, isValid), null, initialCapacity);
}
/// <summary>The value-type counterpart of <see cref="TrackingExtensions"/>, wrapping a sequence of structs in a change-tracking list.</summary>
public static class TrackingExtensionsStruct {
    /// <summary>
    /// Wraps a sequence in a tracking list driven by a custom edit processor.
    /// </summary>
    public static TrackingEditList<T, T, EditableStruct<T, TMetadata>, TMetadata, TEditProcessor> ToTrackingList<T, TMetadata, TEditProcessor>(this IEnumerable<T> source, TEditProcessor editProcessor, Func<T>? newItem = null, int initialCapacity = 4)
        where T : struct where TEditProcessor : IEditProcessor<T, TMetadata> => source.ToTrackingList<T, TMetadata, EditableStruct<T, TMetadata>, TEditProcessor>(editProcessor, newItem ?? (() => new T()), initialCapacity);
    /// <summary>
    /// Creates a TrackingEditList for reference types (classes).
    /// </summary>
    public static TrackingEditList<T, T, EditableStruct<T>> ToTrackingList<T>(this IEnumerable<T> source, Func<T>? newItem = null, int initialCapacity = 4) where T : struct {
        var res = new TrackingEditList<T, T, EditableStruct<T>>(source, initialCapacity);
        newItem ??= () => new T();
        if (newItem is not null)
            res.SetNewItemFactory(newItem);
        return res;
    }
    /// <summary>
    /// Creates a TrackingEditList with metadata support for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableStruct<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<TMetadata?, bool>? isValid = null, Func<T>? newItem = null, int initialCapacity = 4) where T : struct
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, NoOpEditProcessor<T, TMetadata>>(new(), newItem, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(null, null, isValid), newItem, initialCapacity);

    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Validator for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableStruct<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T, object?, TMetadata?> validator, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : struct
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(validator, null), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(validator, null, isValid), null, initialCapacity);
    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Commit for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableStruct<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T, TMetadata?> committer, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : struct
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(null, committer), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(null, committer, isValid), null, initialCapacity);
    /// <summary>
    /// Creates a Validatable TrackingEditList using an external Commit and Validation for reference types.
    /// </summary>
    public static TrackingEditListBase<T, T, EditableStruct<T, TMetadata>> ToTrackingList<T, TMetadata>(this IEnumerable<T> source, Func<T, object?, TMetadata?> validator, Func<T, TMetadata?> committer, Func<TMetadata?, bool>? isValid = null, int initialCapacity = 4) where T : struct
        => isValid is null
            ? source.ToTrackingList<T, TMetadata, DelegateEditProcessorDefault<T, TMetadata>>(new(validator, committer), null, initialCapacity)
            : source.ToTrackingList<T, TMetadata, DelegateEditProcessor<T, TMetadata>>(new(validator, committer, isValid), null, initialCapacity);
}
/// <summary>The do-nothing edit processor, no validation and no commit step, the default when none is given.</summary>
public readonly struct NoOpEditProcessor<TEdit, TMetadata> : IEditProcessor<TEdit, TMetadata> {
    /// <inheritdoc/>
    public bool DoCommit => false;
    /// <inheritdoc/>
    public bool DoValidate => false;
    /// <inheritdoc/>
    public TMetadata? Validate(TEdit? value, object? context) => default;
    /// <inheritdoc/>
    public TMetadata? Commit(TEdit value) => default;
    /// <inheritdoc/>
    public bool IsValid(TMetadata? metadata) => EqualityComparer<TMetadata>.Default.Equals(metadata, default);
}
/// <summary>An edit processor whose validate and commit are supplied as delegates, treating a default metadata as valid.</summary>
public readonly struct DelegateEditProcessorDefault<TEdit, TMetadata>(Func<TEdit?, object?, TMetadata?>? validate, Func<TEdit, TMetadata?>? commit) : IEditProcessor<TEdit, TMetadata> {
    private readonly Func<TEdit?, object?, TMetadata?>? _validate = validate;
    private readonly Func<TEdit, TMetadata?>? _commit = commit;
    /// <inheritdoc/>
    public bool DoCommit => _commit is not null;
    /// <inheritdoc/>
    public bool DoValidate => _validate is not null;
    /// <inheritdoc/>
    public TMetadata? Validate(TEdit? value, object? context) => _validate!(value, context);
    /// <inheritdoc/>
    public TMetadata? Commit(TEdit value) => _commit!(value);
    /// <inheritdoc/>
    public bool IsValid(TMetadata? metadata) => EqualityComparer<TMetadata>.Default.Equals(metadata, default);
}
/// <summary>An edit processor whose validate, commit, and is-valid check are each supplied as a delegate.</summary>
public readonly struct DelegateEditProcessor<TEdit, TMetadata>(Func<TEdit?, object?, TMetadata?>? validate, Func<TEdit, TMetadata?>? commit, Func<TMetadata?, bool> isValid) : IEditProcessor<TEdit, TMetadata> {
    private readonly Func<TEdit?, object?, TMetadata?>? _validate = validate;
    private readonly Func<TEdit, TMetadata?>? _commit = commit;
    private readonly Func<TMetadata?, bool> _isValid = isValid;
    /// <inheritdoc/>
    public bool DoCommit => _commit is not null;
    /// <inheritdoc/>
    public bool DoValidate => _validate is not null;
    /// <inheritdoc/>
    public TMetadata? Validate(TEdit? value, object? context) => _validate!(value, context);
    /// <inheritdoc/>
    public TMetadata? Commit(TEdit value) => _commit!(value);
    /// <inheritdoc/>
    public bool IsValid(TMetadata? metadata) => _isValid(metadata);
}