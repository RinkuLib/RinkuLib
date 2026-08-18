using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Adds metadata reading to a generated tracking type.</summary>
public sealed class RuntimeMetadataReaderCapability<TOriginal, TMetadata> : IRuntimeTrackingCapability<TOriginal> {
    /// <summary>Emits metadata reader behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        builder.AddInterface(typeof(IMetadataReader<TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata);
    }
}

/// <summary>Adds metadata writing to a generated tracking type.</summary>
public sealed class RuntimeMetadataWriterCapability<TOriginal, TMetadata> : IRuntimeTrackingCapability<TOriginal> {
    /// <summary>Emits metadata writer behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        builder.AddInterface(typeof(IMetadataWriter<TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitWriter<TMetadata>(builder, metadata);
    }
}

/// <summary>Adds metadata reading and writing to a generated tracking type.</summary>
public sealed class RuntimeMetadataCapability<TOriginal, TMetadata> : IRuntimeTrackingCapability<TOriginal> {
    /// <summary>Emits metadata reader and writer behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        // IMetadata<TMetadata> already carries both reader and writer contracts.
        builder.AddInterface(typeof(IMetadata<TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata);
        RuntimeMetadataEmitter.EmitWriter<TMetadata>(builder, metadata);
    }
}

internal static class RuntimeMetadataEmitter {
    internal static FieldBuilder GetField<TMetadata>(RuntimeTrackingCapabilityBuilder builder)
        => builder.GetOrAddInstanceField($"metadata:{typeof(TMetadata).AssemblyQualifiedName}", typeof(TMetadata), "metadata");

    internal static void EmitReader<TMetadata>(RuntimeTrackingCapabilityBuilder builder, FieldBuilder metadata, bool preserveContractDefault = false) {
        MethodInfo contract = typeof(IMetadataReader<TMetadata>).GetProperty(nameof(IMetadataReader<TMetadata>.Metadata))!.GetMethod!;
        if (preserveContractDefault && builder.HasDefaultImplementation(contract)) return;
        builder.Implement(contract, il => {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, metadata);
            il.Emit(OpCodes.Ret);
        }, reuseExisting: true);
    }

    internal static void EmitWriter<TMetadata>(RuntimeTrackingCapabilityBuilder builder, FieldBuilder metadata, bool preserveContractDefault = false) {
        MethodInfo contract = typeof(IMetadataWriter<TMetadata>).GetMethod(nameof(IMetadataWriter<TMetadata>.SetMetadata))!;
        if (preserveContractDefault && builder.HasDefaultImplementation(contract)) return;
        builder.Implement(contract, il => {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, metadata);
            builder.EmitRaiseChanged(il, nameof(IMetadataReader<TMetadata>.Metadata));
            il.Emit(OpCodes.Ret);
        }, reuseExisting: true);
    }
}
