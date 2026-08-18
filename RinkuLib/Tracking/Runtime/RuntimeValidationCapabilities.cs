using System;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking.Runtime;

// Validation is expressed only in terms of TEdit. If it needs the original, TEdit exposes
// ITrackingItem<TOriginal>.HasOriginal. This keeps handwritten and generated items on one contract.
/// <summary>Validates a generated edit.</summary>
public delegate bool RuntimeValidationHandler<in TEdit>(TEdit item);
/// <summary>Validates a generated edit with context.</summary>
public delegate bool RuntimeContextValidationHandler<in TEdit, in TContext>(TEdit item, TContext context);
/// <summary>Validates a generated edit and returns metadata.</summary>
public delegate bool RuntimeValidationHandler<in TEdit, TMetadata>(TEdit item, out TMetadata metadata);
/// <summary>Validates a generated edit with context and returns metadata.</summary>
public delegate bool RuntimeValidationHandler<in TEdit, in TContext, TMetadata>(TEdit item, TContext context, out TMetadata metadata);
/// <summary>Validates a generated edit asynchronously.</summary>
public delegate ValueTask<bool> RuntimeAsyncValidationHandler<in TEdit>(TEdit item, CancellationToken cancellationToken);
/// <summary>Validates a generated edit with context asynchronously.</summary>
public delegate ValueTask<bool> RuntimeAsyncContextValidationHandler<in TEdit, in TContext>(TEdit item, TContext context, CancellationToken cancellationToken);
/// <summary>Validates a generated edit asynchronously and returns metadata.</summary>
public delegate ValueTask<ValidationOutcome<TMetadata>> RuntimeAsyncValidationHandler<in TEdit, TMetadata>(
    TEdit item, CancellationToken cancellationToken);
/// <summary>Validates a generated edit with context asynchronously and returns metadata.</summary>
public delegate ValueTask<ValidationOutcome<TMetadata>> RuntimeAsyncValidationHandler<in TEdit, in TContext, TMetadata>(
    TEdit item, TContext context, CancellationToken cancellationToken);

/// <summary>Adds synchronous validation to a generated tracking type.</summary>
public sealed class RuntimeValidationCapability<TOriginal, TEdit> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeValidationHandler<TEdit>> _validate;

    /// <summary>Creates synchronous validation.</summary>
    public RuntimeValidationCapability(RuntimeValidationHandler<TEdit> validate) => _validate = new(validate);
    /// <summary>Creates synchronous validation from a method.</summary>
    public RuntimeValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits synchronous validation behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IValidatable).GetMethod(nameof(IValidatable.Validate))!;
        if (builder.HasDefaultImplementation(contract)) return;
        builder.AddInterface(typeof(IValidatable));
        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
            }, "validation");
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds contextual synchronous validation to a generated tracking type.</summary>
public sealed class RuntimeContextValidationCapability<TOriginal, TEdit, TContext> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeContextValidationHandler<TEdit, TContext>> _validate;

    /// <summary>Creates contextual validation.</summary>
    public RuntimeContextValidationCapability(RuntimeContextValidationHandler<TEdit, TContext> validate) => _validate = new(validate);
    /// <summary>Creates contextual validation from a method.</summary>
    public RuntimeContextValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits contextual validation behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IValidatable<TContext>).GetMethod(nameof(IValidatable<TContext>.Validate))!;
        if (builder.HasDefaultImplementation(contract)) return;
        builder.AddInterface(typeof(IValidatable<TContext>));
        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
            }, "contextValidation");
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds asynchronous validation to a generated tracking type.</summary>
public sealed class RuntimeAsyncValidationCapability<TOriginal, TEdit> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeAsyncValidationHandler<TEdit>> _validate;

    /// <summary>Creates an asynchronous validation capability.</summary>
    public RuntimeAsyncValidationCapability(RuntimeAsyncValidationHandler<TEdit> validate) => _validate = new(validate);
    /// <summary>Creates an asynchronous validation capability from a method.</summary>
    public RuntimeAsyncValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits asynchronous validation behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IAsyncValidatable).GetMethod(nameof(IAsyncValidatable.ValidateAsync))!;
        if (builder.HasDefaultImplementation(contract)) return;
        builder.AddInterface(typeof(IAsyncValidatable));
        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
            }, "asyncValidation");
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds contextual asynchronous validation to a generated tracking type.</summary>
public sealed class RuntimeAsyncContextValidationCapability<TOriginal, TEdit, TContext> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeAsyncContextValidationHandler<TEdit, TContext>> _validate;

    /// <summary>Creates a contextual asynchronous validation capability.</summary>
    public RuntimeAsyncContextValidationCapability(RuntimeAsyncContextValidationHandler<TEdit, TContext> validate) => _validate = new(validate);
    /// <summary>Creates a contextual asynchronous validation capability from a method.</summary>
    public RuntimeAsyncContextValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits contextual asynchronous validation behavior.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IAsyncValidatable<TContext>).GetMethod(nameof(IAsyncValidatable<TContext>.ValidateAsync))!;
        if (builder.HasDefaultImplementation(contract)) return;
        builder.AddInterface(typeof(IAsyncValidatable<TContext>));
        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
                emit.Emit(OpCodes.Ldarg_2);
            }, "asyncContextValidation");
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds synchronous validation with metadata.</summary>
public sealed class RuntimeValidationCapability<TOriginal, TEdit, TMetadata> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeValidationHandler<TEdit, TMetadata>> _validate;

    /// <summary>Creates synchronous validation with metadata.</summary>
    public RuntimeValidationCapability(RuntimeValidationHandler<TEdit, TMetadata> validate) => _validate = new(validate);
    /// <summary>Creates synchronous validation with metadata from a method.</summary>
    public RuntimeValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits synchronous validation with metadata.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IValidatable).GetMethod(nameof(IValidatable.Validate))!;
        if (builder.HasDefaultImplementation(contract)) return;
        // The strongest bundle already inherits IValidatable + IMetadataReader<TMetadata>.
        builder.AddInterface(typeof(IValidation<TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata, preserveContractDefault: true);

        builder.Implement(contract, il => {
            LocalBuilder result = il.DeclareLocal(typeof(TMetadata));
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldloca, result);
            }, "validation");
            RuntimeValidationEmitter.EmitStoreMetadata(builder, il, metadata, result);
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds contextual synchronous validation with metadata.</summary>
public sealed class RuntimeValidationCapability<TOriginal, TEdit, TContext, TMetadata> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeValidationHandler<TEdit, TContext, TMetadata>> _validate;

    /// <summary>Creates contextual validation with metadata.</summary>
    public RuntimeValidationCapability(RuntimeValidationHandler<TEdit, TContext, TMetadata> validate) => _validate = new(validate);
    /// <summary>Creates contextual validation with metadata from a method.</summary>
    public RuntimeValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits contextual validation with metadata.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IValidatable<TContext>).GetMethod(nameof(IValidatable<TContext>.Validate))!;
        if (builder.HasDefaultImplementation(contract)) return;
        // The strongest bundle already inherits contextual validation + metadata reader.
        builder.AddInterface(typeof(IValidation<TContext, TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata, preserveContractDefault: true);

        builder.Implement(contract, il => {
            LocalBuilder result = il.DeclareLocal(typeof(TMetadata));
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
                emit.Emit(OpCodes.Ldloca, result);
            }, "contextValidation");
            RuntimeValidationEmitter.EmitStoreMetadata(builder, il, metadata, result);
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds asynchronous validation with metadata.</summary>
public sealed class RuntimeAsyncValidationCapability<TOriginal, TEdit, TMetadata> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeAsyncValidationHandler<TEdit, TMetadata>> _validate;

    /// <summary>Creates asynchronous validation with metadata.</summary>
    public RuntimeAsyncValidationCapability(RuntimeAsyncValidationHandler<TEdit, TMetadata> validate) => _validate = new(validate);
    /// <summary>Creates asynchronous validation with metadata from a method.</summary>
    public RuntimeAsyncValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits asynchronous validation with metadata.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IAsyncValidatable).GetMethod(nameof(IAsyncValidatable.ValidateAsync))!;
        if (builder.HasDefaultImplementation(contract)) return;
        // The strongest bundle already inherits async validation + metadata reader.
        builder.AddInterface(typeof(IAsyncValidation<TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata, preserveContractDefault: true);
        FieldBuilder setter = RuntimeValidationEmitter.EmitAsyncMetadataSetter<TEdit, TMetadata>(builder, metadata);

        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
            }, "asyncValidation");
            RuntimeValidationEmitter.EmitCompleteAsync<TEdit, TMetadata>(il, setter);
            il.Emit(OpCodes.Ret);
        });
    }
}

/// <summary>Adds contextual asynchronous validation with metadata.</summary>
public sealed class RuntimeAsyncValidationCapability<TOriginal, TEdit, TContext, TMetadata> : IRuntimeTrackingCapability<TOriginal>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly RuntimeCall<RuntimeAsyncValidationHandler<TEdit, TContext, TMetadata>> _validate;

    /// <summary>Creates contextual asynchronous validation with metadata.</summary>
    public RuntimeAsyncValidationCapability(RuntimeAsyncValidationHandler<TEdit, TContext, TMetadata> validate) => _validate = new(validate);
    /// <summary>Creates contextual asynchronous validation with metadata from a method.</summary>
    public RuntimeAsyncValidationCapability(MethodInfo method, object? target = null) => _validate = new(method, target);

    /// <summary>Emits contextual asynchronous validation with metadata.</summary>
    public void Emit(RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IAsyncValidatable<TContext>).GetMethod(nameof(IAsyncValidatable<TContext>.ValidateAsync))!;
        if (builder.HasDefaultImplementation(contract)) return;
        // The strongest bundle already inherits contextual async validation + metadata reader.
        builder.AddInterface(typeof(IAsyncValidation<TContext, TMetadata>));
        FieldBuilder metadata = RuntimeMetadataEmitter.GetField<TMetadata>(builder);
        RuntimeMetadataEmitter.EmitReader<TMetadata>(builder, metadata, preserveContractDefault: true);
        FieldBuilder setter = RuntimeValidationEmitter.EmitAsyncMetadataSetter<TEdit, TMetadata>(builder, metadata);

        builder.Implement(contract, il => {
            _validate.Emit(builder, il, emit => {
                emit.Emit(OpCodes.Ldarg_0);
                emit.Emit(OpCodes.Castclass, typeof(TEdit));
                emit.Emit(OpCodes.Ldarg_1);
                emit.Emit(OpCodes.Ldarg_2);
            }, "asyncContextValidation");
            RuntimeValidationEmitter.EmitCompleteAsync<TEdit, TMetadata>(il, setter);
            il.Emit(OpCodes.Ret);
        });
    }
}

internal static class RuntimeValidationEmitter {
    private static readonly MethodInfo CompleteAsyncMethod = typeof(RuntimeAsyncValidationCompletion)
        .GetMethod(nameof(RuntimeAsyncValidationCompletion.Complete), BindingFlags.Static | BindingFlags.Public)!;

    // The bool returned by the validation call is already on the stack.
    internal static void EmitStoreMetadata(RuntimeTrackingCapabilityBuilder builder, ILGenerator il, FieldBuilder metadata, LocalBuilder value) {
        LocalBuilder valid = il.DeclareLocal(typeof(bool));
        il.Emit(OpCodes.Stloc, valid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stfld, metadata);
        builder.EmitRaiseChanged(il, "Metadata");
        il.Emit(OpCodes.Ldloc, valid);
    }

    internal static FieldBuilder EmitAsyncMetadataSetter<TEdit, TMetadata>(
        RuntimeTrackingCapabilityBuilder builder, FieldBuilder metadata) {
        MethodBuilder method = builder.DefineStaticMethod("setValidationMetadata", typeof(void), typeof(TEdit), typeof(TMetadata));
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, builder.TypeBuilder);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, metadata);
        builder.EmitRaiseChanged(il, "Metadata", emit => {
            emit.Emit(OpCodes.Ldarg_0);
            emit.Emit(OpCodes.Castclass, builder.TypeBuilder);
        });
        il.Emit(OpCodes.Ret);

        Type setterType = typeof(Action<TEdit, TMetadata>);
        string methodName = method.Name;
        return builder.DefineInitializedStaticField(setterType, generatedType => {
            MethodInfo generatedMethod = generatedType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
            return generatedMethod.CreateDelegate(setterType);
        }, "validationMetadataSetter");
    }

    internal static void EmitCompleteAsync<TEdit, TMetadata>(ILGenerator il, FieldBuilder setter) {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldsfld, setter);
        il.Emit(OpCodes.Call, CompleteAsyncMethod.MakeGenericMethod(typeof(TEdit), typeof(TMetadata)));
    }
}

/// <summary>Completes asynchronous validation results.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RuntimeAsyncValidationCompletion {
    /// <summary>Completes an asynchronous validation result.</summary>
    public static ValueTask<bool> Complete<TEdit, TMetadata>(
        ValueTask<ValidationOutcome<TMetadata>> pending,
        TEdit item,
        Action<TEdit, TMetadata> setMetadata) {
        if (pending.IsCompletedSuccessfully) {
            ValidationOutcome<TMetadata> outcome = pending.Result;
            setMetadata(item, outcome.Metadata);
            return new(outcome.IsValid);
        }
        return Awaited(pending, item, setMetadata);
    }

    private static async ValueTask<bool> Awaited<TEdit, TMetadata>(
        ValueTask<ValidationOutcome<TMetadata>> pending,
        TEdit item,
        Action<TEdit, TMetadata> setMetadata) {
        ValidationOutcome<TMetadata> outcome = await pending.ConfigureAwait(false);
        setMetadata(item, outcome.Metadata);
        return outcome.IsValid;
    }
}
