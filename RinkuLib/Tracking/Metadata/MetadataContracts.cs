namespace Rinku.Tracking;

/// <summary>Exposes metadata.</summary>
public interface IMetadataReader<out TMetadata>
{
    /// <summary>Gets the metadata.</summary>
    TMetadata Metadata { get; }
}

/// <summary>Stores metadata.</summary>
public interface IMetadataWriter<in TMetadata>
{
    /// <summary>Stores metadata.</summary>
    void SetMetadata(TMetadata metadata);
}

/// <summary>Reads and writes metadata.</summary>
public interface IMetadata<TMetadata> : IMetadataReader<TMetadata>, IMetadataWriter<TMetadata> { }
