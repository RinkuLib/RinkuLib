namespace Rinku.Tracking;

/// <summary>Reads metadata from a tracked value.</summary>
public interface IMetadataReader<out TMetadata> {
    /// <summary>Gets the metadata.</summary>
    TMetadata Metadata { get; }
}

/// <summary>Writes metadata to a tracked value.</summary>
public interface IMetadataWriter<in TMetadata> {
    /// <summary>Stores metadata.</summary>
    void SetMetadata(TMetadata metadata);
}

// Optional nominal intersection. Reader and writer remain independently usable.
/// <summary>Combines metadata reading and writing.</summary>
public interface IMetadata<TMetadata> : IMetadataReader<TMetadata>, IMetadataWriter<TMetadata> { }
