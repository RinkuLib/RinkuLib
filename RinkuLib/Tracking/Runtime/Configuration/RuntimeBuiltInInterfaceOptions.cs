using System.ComponentModel;
using Rinku.Tracking.Binding;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeBuiltInInterfaceOptions
{
    internal static bool TryApply<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, Type contract)
    {
        if (contract == typeof(IEditable) ||
            contract == typeof(ITrackingListNewState) ||
            contract == typeof(IRuntimeMemberAccess) ||
            contract == typeof(ITrackingChanges) ||
            contract == typeof(IRuntimeNewStateControl) ||
            contract == typeof(IRuntimeTrackingItem<TOriginal>) ||
            contract == typeof(IOriginal<TOriginal>))
            return true;

        if (contract == typeof(INotifyPropertyChanged) || contract == typeof(IEditableObject))
        {
            RuntimeBindingOption<TOriginal>.Instance.Apply(type);
            return true;
        }

        if (!contract.IsGenericType) return false;
        Type definition = contract.GetGenericTypeDefinition();
        Type argument = contract.GetGenericArguments()[0];
        if (definition == typeof(IMetadataReader<>))
        {
            Metadata(type, argument, reader: true, writer: false);
            return true;
        }
        if (definition == typeof(IMetadataWriter<>))
        {
            Metadata(type, argument, reader: false, writer: true);
            return true;
        }
        if (definition == typeof(IMetadata<>))
        {
            Metadata(type, argument, reader: true, writer: true);
            return true;
        }

        return false;
    }

    private static void Metadata<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, Type metadataType, bool reader, bool writer)
    {
        IRuntimeMetadataEmitterConfiguration? configuration = null;
        for (int i = 0; i < type.TypeEmitters.Count; i++)
        {
            if (type.TypeEmitters[i] is not IRuntimeMetadataEmitterConfiguration current || current.MetadataType != metadataType) continue;
            configuration = current;
            break;
        }

        if (configuration is null)
        {
            Type emitterType = typeof(RuntimeMetadataEmitter<,>).MakeGenericType(typeof(TOriginal), metadataType);
            configuration = (IRuntimeMetadataEmitterConfiguration)(Activator.CreateInstance(emitterType, nonPublic: true)
                ?? throw new InvalidOperationException($"Unable to create metadata emitter {emitterType}."));
            type.AddTypeEmitter((IRuntimeTrackingTypeEmitter<TOriginal>)configuration);
        }

        if (reader) configuration.RequireReader();
        if (writer) configuration.RequireWriter();
    }
}
