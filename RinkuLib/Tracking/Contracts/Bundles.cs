namespace Rinku.Tracking;

// Optional nominal intersections for APIs/extensions that repeatedly need the same capability set.
/// <summary>Combines editing with metadata reading.</summary>
public interface IEditableMetadataReaderTrackingItem<TOriginal, out TMetadata> :
    IEditableTrackingItem<TOriginal>, IMetadataReader<TMetadata> { }

/// <summary>Combines editing with metadata writing.</summary>
public interface IEditableMetadataWriterTrackingItem<TOriginal, in TMetadata> :
    IEditableTrackingItem<TOriginal>, IMetadataWriter<TMetadata> { }

/// <summary>Combines editing with metadata reading and writing.</summary>
public interface IEditableMetadataTrackingItem<TOriginal, TMetadata> :
    IEditableMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IEditableMetadataWriterTrackingItem<TOriginal, TMetadata>,
    IMetadata<TMetadata> { }

/// <summary>Combines editing with synchronous validation.</summary>
public interface IEditableValidationTrackingItem<TOriginal, out TMetadata> :
    IEditableMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IValidation<TMetadata> { }

/// <summary>Combines editing with contextual synchronous validation.</summary>
public interface IEditableValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IValidation<TContext, TMetadata> { }

/// <summary>Combines editing with both synchronous validation forms.</summary>
public interface IEditableDualValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableValidationTrackingItem<TOriginal, TMetadata>,
    IEditableValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IDualValidation<TContext, TMetadata> { }

/// <summary>Combines editing with asynchronous validation.</summary>
public interface IEditableAsyncValidationTrackingItem<TOriginal, out TMetadata> :
    IEditableMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IAsyncValidation<TMetadata> { }

/// <summary>Combines editing with contextual asynchronous validation.</summary>
public interface IEditableAsyncValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IAsyncValidation<TContext, TMetadata> { }

/// <summary>Combines editing with both asynchronous validation forms.</summary>
public interface IEditableAsyncDualValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableAsyncValidationTrackingItem<TOriginal, TMetadata>,
    IEditableAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IAsyncDualValidation<TContext, TMetadata> { }

/// <summary>Combines editing with synchronous and asynchronous validation.</summary>
public interface IEditableSyncAsyncValidationTrackingItem<TOriginal, out TMetadata> :
    IEditableValidationTrackingItem<TOriginal, TMetadata>,
    IEditableAsyncValidationTrackingItem<TOriginal, TMetadata>,
    ISyncAsyncValidation<TMetadata> { }

/// <summary>Combines editing with contextual synchronous and asynchronous validation.</summary>
public interface IEditableSyncAsyncValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    ISyncAsyncValidation<TContext, TMetadata> { }

/// <summary>Combines editing with all validation forms.</summary>
public interface IEditableCompleteValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IEditableDualValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableAsyncDualValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableSyncAsyncValidationTrackingItem<TOriginal, TMetadata>,
    IEditableSyncAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    ICompleteValidation<TContext, TMetadata> { }
