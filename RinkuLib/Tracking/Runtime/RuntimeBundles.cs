namespace Rinku.Tracking.Runtime;

// Runtime bundles only name useful capability intersections. They carry no list/creation behavior.
/// <summary>Combines runtime tracking with metadata reading.</summary>
public interface IRuntimeMetadataReaderTrackingItem<TOriginal, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>, IEditableMetadataReaderTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with metadata writing.</summary>
public interface IRuntimeMetadataWriterTrackingItem<TOriginal, in TMetadata> :
    IRuntimeTrackingItem<TOriginal>, IEditableMetadataWriterTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with metadata reading and writing.</summary>
public interface IRuntimeMetadataTrackingItem<TOriginal, TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IRuntimeMetadataWriterTrackingItem<TOriginal, TMetadata>,
    IEditableMetadataTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with synchronous validation.</summary>
public interface IRuntimeValidationTrackingItem<TOriginal, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IEditableValidationTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with contextual synchronous validation.</summary>
public interface IRuntimeValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IEditableValidationTrackingItem<TOriginal, TContext, TMetadata> { }

/// <summary>Combines runtime tracking with both synchronous validation forms.</summary>
public interface IRuntimeDualValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeValidationTrackingItem<TOriginal, TMetadata>,
    IRuntimeValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableDualValidationTrackingItem<TOriginal, TContext, TMetadata> { }

/// <summary>Combines runtime tracking with asynchronous validation.</summary>
public interface IRuntimeAsyncValidationTrackingItem<TOriginal, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IEditableAsyncValidationTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with contextual asynchronous validation.</summary>
public interface IRuntimeAsyncValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeMetadataReaderTrackingItem<TOriginal, TMetadata>,
    IEditableAsyncValidationTrackingItem<TOriginal, TContext, TMetadata> { }

/// <summary>Combines runtime tracking with both asynchronous validation forms.</summary>
public interface IRuntimeAsyncDualValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeAsyncValidationTrackingItem<TOriginal, TMetadata>,
    IRuntimeAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableAsyncDualValidationTrackingItem<TOriginal, TContext, TMetadata> { }

/// <summary>Combines runtime tracking with synchronous and asynchronous validation.</summary>
public interface IRuntimeSyncAsyncValidationTrackingItem<TOriginal, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeValidationTrackingItem<TOriginal, TMetadata>,
    IRuntimeAsyncValidationTrackingItem<TOriginal, TMetadata>,
    IEditableSyncAsyncValidationTrackingItem<TOriginal, TMetadata> { }

/// <summary>Combines runtime tracking with contextual synchronous and asynchronous validation.</summary>
public interface IRuntimeSyncAsyncValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IRuntimeAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableSyncAsyncValidationTrackingItem<TOriginal, TContext, TMetadata> { }

/// <summary>Combines runtime tracking with all validation forms.</summary>
public interface IRuntimeCompleteValidationTrackingItem<TOriginal, in TContext, out TMetadata> :
    IRuntimeTrackingItem<TOriginal>,
    IRuntimeDualValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IRuntimeAsyncDualValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IRuntimeSyncAsyncValidationTrackingItem<TOriginal, TMetadata>,
    IRuntimeSyncAsyncValidationTrackingItem<TOriginal, TContext, TMetadata>,
    IEditableCompleteValidationTrackingItem<TOriginal, TContext, TMetadata> { }
