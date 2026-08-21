using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking.Runtime;

/// <summary>Validates a generated edit.</summary>
public delegate bool RuntimeValidationHandler<in TEdit>(TEdit edit);
/// <summary>Validates a generated edit with caller data.</summary>
public delegate bool RuntimeContextValidationHandler<in TEdit, in TContext>(TEdit edit, TContext context);
/// <summary>Validates a generated edit asynchronously.</summary>
public delegate ValueTask<bool> RuntimeAsyncValidationHandler<in TEdit>(TEdit edit, CancellationToken cancellationToken);
/// <summary>Validates a generated edit asynchronously with caller data.</summary>
public delegate ValueTask<bool> RuntimeAsyncContextValidationHandler<in TEdit, in TContext>(TEdit edit, TContext context, CancellationToken cancellationToken);

/// <summary>Provides validation options for generated tracking types.</summary>
public static class RuntimeValidationOptionsExtensions
{
    /// <summary>Adds synchronous validation.</summary>
    public static RuntimeTrackingOptions<TOriginal> Validate<TOriginal, TEdit>(this RuntimeTrackingOptions<TOriginal> options, RuntimeValidationHandler<TEdit> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeValidationOption<TOriginal, TEdit>(validate));
    }

    /// <summary>Adds synchronous validation with caller data.</summary>
    public static RuntimeTrackingOptions<TOriginal> Validate<TOriginal, TEdit, TContext>(this RuntimeTrackingOptions<TOriginal> options, RuntimeContextValidationHandler<TEdit, TContext> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeContextValidationOption<TOriginal, TEdit, TContext>(validate));
    }

    /// <summary>Adds asynchronous validation.</summary>
    public static RuntimeTrackingOptions<TOriginal> ValidateAsync<TOriginal, TEdit>(this RuntimeTrackingOptions<TOriginal> options, RuntimeAsyncValidationHandler<TEdit> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeAsyncValidationOption<TOriginal, TEdit>(validate));
    }

    /// <summary>Adds asynchronous validation with caller data.</summary>
    public static RuntimeTrackingOptions<TOriginal> ValidateAsync<TOriginal, TEdit, TContext>(this RuntimeTrackingOptions<TOriginal> options, RuntimeAsyncContextValidationHandler<TEdit, TContext> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeAsyncContextValidationOption<TOriginal, TEdit, TContext>(validate));
    }
}

internal sealed class RuntimeValidationOption<TOriginal, TEdit>(RuntimeValidationHandler<TEdit> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        EnsureEditContract(type);
        Type contract = typeof(IValidatable);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IValidatable.Validate))
            ?? throw new MissingMethodException(contract.FullName, nameof(IValidatable.Validate));
        type.GetOrAddMethod(method).Emitter = new RuntimeValidationMethodEmitter<TOriginal, TEdit>(handler);
    }

    private static void EnsureEditContract(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
    }
}

internal sealed class RuntimeContextValidationOption<TOriginal, TEdit, TContext>(RuntimeContextValidationHandler<TEdit, TContext> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IValidatable<TContext>);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IValidatable<TContext>.Validate))
            ?? throw new MissingMethodException(contract.FullName, nameof(IValidatable<TContext>.Validate));
        type.GetOrAddMethod(method).Emitter = new RuntimeContextValidationMethodEmitter<TOriginal, TEdit, TContext>(handler);
    }
}

internal sealed class RuntimeAsyncValidationOption<TOriginal, TEdit>(RuntimeAsyncValidationHandler<TEdit> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IAsyncValidatable);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IAsyncValidatable.ValidateAsync))
            ?? throw new MissingMethodException(contract.FullName, nameof(IAsyncValidatable.ValidateAsync));
        type.GetOrAddMethod(method).Emitter = new RuntimeAsyncValidationMethodEmitter<TOriginal, TEdit>(handler);
    }
}

internal sealed class RuntimeAsyncContextValidationOption<TOriginal, TEdit, TContext>(RuntimeAsyncContextValidationHandler<TEdit, TContext> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IAsyncValidatable<TContext>);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IAsyncValidatable<TContext>.ValidateAsync))
            ?? throw new MissingMethodException(contract.FullName, nameof(IAsyncValidatable<TContext>.ValidateAsync));
        type.GetOrAddMethod(method).Emitter = new RuntimeAsyncContextValidationMethodEmitter<TOriginal, TEdit, TContext>(handler);
    }
}

internal sealed class RuntimeValidationMethodEmitter<TOriginal, TEdit>(RuntimeValidationHandler<TEdit> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_{index}", typeof(RuntimeValidationHandler<TEdit>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = Define(context, method.Requirement, index);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Callvirt, typeof(RuntimeValidationHandler<TEdit>).GetMethod(nameof(RuntimeValidationHandler<TEdit>.Invoke))
            ?? throw new MissingMethodException(typeof(RuntimeValidationHandler<TEdit>).FullName, nameof(RuntimeValidationHandler<TEdit>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }

    private static MethodBuilder Define(RuntimeTrackingEmitContext<TOriginal> context, MethodInfo contract, int index)
        => context.TypeBuilder.DefineMethod($"__validation_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, contract.ReturnType, Type.EmptyTypes);
}

internal sealed class RuntimeContextValidationMethodEmitter<TOriginal, TEdit, TContext>(RuntimeContextValidationHandler<TEdit, TContext> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_context_{index}", typeof(RuntimeContextValidationHandler<TEdit, TContext>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_context_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(bool), [typeof(TContext)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(RuntimeContextValidationHandler<TEdit, TContext>).GetMethod(nameof(RuntimeContextValidationHandler<TEdit, TContext>.Invoke))
            ?? throw new MissingMethodException(typeof(RuntimeContextValidationHandler<TEdit, TContext>).FullName, nameof(RuntimeContextValidationHandler<TEdit, TContext>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}

internal sealed class RuntimeAsyncValidationMethodEmitter<TOriginal, TEdit>(RuntimeAsyncValidationHandler<TEdit> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_async_{index}", typeof(RuntimeAsyncValidationHandler<TEdit>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_async_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(ValueTask<bool>), [typeof(CancellationToken)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(RuntimeAsyncValidationHandler<TEdit>).GetMethod(nameof(RuntimeAsyncValidationHandler<TEdit>.Invoke))
            ?? throw new MissingMethodException(typeof(RuntimeAsyncValidationHandler<TEdit>).FullName, nameof(RuntimeAsyncValidationHandler<TEdit>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}

internal sealed class RuntimeAsyncContextValidationMethodEmitter<TOriginal, TEdit, TContext>(RuntimeAsyncContextValidationHandler<TEdit, TContext> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_async_context_{index}", typeof(RuntimeAsyncContextValidationHandler<TEdit, TContext>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_async_context_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(ValueTask<bool>), [typeof(TContext), typeof(CancellationToken)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(RuntimeAsyncContextValidationHandler<TEdit, TContext>).GetMethod(nameof(RuntimeAsyncContextValidationHandler<TEdit, TContext>.Invoke))
            ?? throw new MissingMethodException(typeof(RuntimeAsyncContextValidationHandler<TEdit, TContext>).FullName, nameof(RuntimeAsyncContextValidationHandler<TEdit, TContext>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}
