namespace Rinku.Tracking.Runtime;

/// <summary>Provides metadata options for generated tracking types.</summary>
public static class RuntimeMetadataOptionsExtensions
{
    /// <summary>Adds metadata reading and writing capabilities.</summary>
    public static RuntimeTrackingOptions<TOriginal> Metadata<TOriginal, TMetadata>(
        this RuntimeTrackingOptions<TOriginal> options,
        bool reader = true,
        bool writer = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Add(new RuntimeMetadataOption<TOriginal, TMetadata>(reader, writer));
    }
}

internal sealed class RuntimeMetadataOption<TOriginal, TMetadata>(bool reader, bool writer) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (reader) new RuntimeInterfaceOption<TOriginal>(typeof(IMetadataReader<TMetadata>)).Apply(type);
        if (writer) new RuntimeInterfaceOption<TOriginal>(typeof(IMetadataWriter<TMetadata>)).Apply(type);
    }
}
