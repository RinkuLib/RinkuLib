using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking.Runtime;

/// <summary>Provides validation options for generated tracking types.</summary>
public static class RuntimeValidationOptionsExtensions
{
    /// <summary>Adds synchronous validation.</summary>
    public static RuntimeTrackingOptions<TOriginal> Validate<TOriginal, TEdit>(this RuntimeTrackingOptions<TOriginal> options, Func<TEdit, bool> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeValidationOption<TOriginal, TEdit>(validate));
    }

    /// <summary>Adds synchronous validation with caller data.</summary>
    public static RuntimeTrackingOptions<TOriginal> Validate<TOriginal, TEdit, TContext>(this RuntimeTrackingOptions<TOriginal> options, Func<TEdit, TContext, bool> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeContextValidationOption<TOriginal, TEdit, TContext>(validate));
    }

    /// <summary>Adds validation that can reject a persistence operation result before tracking confirmation.</summary>
    public static RuntimeTrackingOptions<TOriginal> ValidateResult<TOriginal, TEdit, TResult>(this RuntimeTrackingOptions<TOriginal> options, Func<TEdit, TResult, bool> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeResultValidationOption<TOriginal, TEdit, TResult>(validate));
    }

    /// <summary>Adds asynchronous validation.</summary>
    public static RuntimeTrackingOptions<TOriginal> ValidateAsync<TOriginal, TEdit>(this RuntimeTrackingOptions<TOriginal> options, Func<TEdit, CancellationToken, ValueTask<bool>> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeAsyncValidationOption<TOriginal, TEdit>(validate));
    }

    /// <summary>Adds asynchronous validation with caller data.</summary>
    public static RuntimeTrackingOptions<TOriginal> ValidateAsync<TOriginal, TEdit, TContext>(this RuntimeTrackingOptions<TOriginal> options, Func<TEdit, TContext, CancellationToken, ValueTask<bool>> validate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        return options.Add(new RuntimeAsyncContextValidationOption<TOriginal, TEdit, TContext>(validate));
    }
}

internal sealed class RuntimeValidationOption<TOriginal, TEdit>(Func<TEdit, bool> handler) : IRuntimeTrackingOption<TOriginal>
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

internal sealed class RuntimeContextValidationOption<TOriginal, TEdit, TContext>(Func<TEdit, TContext, bool> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IValidatable<TContext>);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IValidatable<>.Validate))
            ?? throw new MissingMethodException(contract.FullName, nameof(IValidatable<>.Validate));
        type.GetOrAddMethod(method).Emitter = new RuntimeContextValidationMethodEmitter<TOriginal, TEdit, TContext>(handler);
    }
}

internal sealed class RuntimeResultValidationOption<TOriginal, TEdit, TResult>(Func<TEdit, TResult, bool> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IResultValidatable<TResult>);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IResultValidatable<>.ValidateResult))
            ?? throw new MissingMethodException(contract.FullName, nameof(IResultValidatable<>.ValidateResult));
        type.GetOrAddMethod(method).Emitter = new RuntimeContextValidationMethodEmitter<TOriginal, TEdit, TResult>(handler);
    }
}

internal sealed class RuntimeAsyncValidationOption<TOriginal, TEdit>(Func<TEdit, CancellationToken, ValueTask<bool>> handler) : IRuntimeTrackingOption<TOriginal>
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

internal sealed class RuntimeAsyncContextValidationOption<TOriginal, TEdit, TContext>(Func<TEdit, TContext, CancellationToken, ValueTask<bool>> handler) : IRuntimeTrackingOption<TOriginal>
{
    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        if (!typeof(TEdit).IsInterface)
            throw new InvalidOperationException($"Validation edit contract {typeof(TEdit)} must be an interface implemented by the generated type.");
        new RuntimeInterfaceOption<TOriginal>(typeof(TEdit)).Apply(type);
        Type contract = typeof(IAsyncValidatable<TContext>);
        type.RequireInterface(contract);
        MethodInfo method = contract.GetMethod(nameof(IAsyncValidatable<>.ValidateAsync))
            ?? throw new MissingMethodException(contract.FullName, nameof(IAsyncValidatable<>.ValidateAsync));
        type.GetOrAddMethod(method).Emitter = new RuntimeAsyncContextValidationMethodEmitter<TOriginal, TEdit, TContext>(handler);
    }
}

internal sealed class RuntimeValidationMethodEmitter<TOriginal, TEdit>(Func<TEdit, bool> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_{index}", typeof(Func<TEdit, bool>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = Define(context, method.Requirement, index);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Callvirt, typeof(Func<TEdit, bool>).GetMethod(nameof(Func<,>.Invoke))
            ?? throw new MissingMethodException(typeof(Func<TEdit, bool>).FullName, nameof(Func<,>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }

    private static MethodBuilder Define(RuntimeTrackingEmitContext<TOriginal> context, MethodInfo contract, int index)
        => context.TypeBuilder.DefineMethod($"__validation_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, contract.ReturnType, Type.EmptyTypes);
}

internal sealed class RuntimeContextValidationMethodEmitter<TOriginal, TEdit, TContext>(Func<TEdit, TContext, bool> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_context_{index}", typeof(Func<TEdit, TContext, bool>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_context_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(bool), [typeof(TContext)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Func<TEdit, TContext, bool>).GetMethod(nameof(Func<,,>.Invoke))
            ?? throw new MissingMethodException(typeof(Func<TEdit, TContext, bool>).FullName, nameof(Func<,,>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}

internal sealed class RuntimeAsyncValidationMethodEmitter<TOriginal, TEdit>(Func<TEdit, CancellationToken, ValueTask<bool>> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_async_{index}", typeof(Func<TEdit, CancellationToken, ValueTask<bool>>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_async_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(ValueTask<bool>), [typeof(CancellationToken)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Func<TEdit, CancellationToken, ValueTask<bool>>).GetMethod(nameof(Func<,,>.Invoke))
            ?? throw new MissingMethodException(typeof(Func<TEdit, CancellationToken, ValueTask<bool>>).FullName, nameof(Func<,,>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}

internal sealed class RuntimeAsyncContextValidationMethodEmitter<TOriginal, TEdit, TContext>(Func<TEdit, TContext, CancellationToken, ValueTask<bool>> handler) : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        FieldBuilder field = context.TypeBuilder.DefineField($"s_validate_async_context_{index}", typeof(Func<TEdit, TContext, CancellationToken, ValueTask<bool>>), FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => (type.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, field.Name)).SetValue(null, handler));
        MethodBuilder generated = context.TypeBuilder.DefineMethod($"__validation_async_context_{index}", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot, typeof(ValueTask<bool>), [typeof(TContext), typeof(CancellationToken)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Func<TEdit, TContext, CancellationToken, ValueTask<bool>>).GetMethod(nameof(Func<,,,>.Invoke))
            ?? throw new MissingMethodException(typeof(Func<TEdit, TContext, CancellationToken, ValueTask<bool>>).FullName, nameof(Func<,,,>.Invoke)));
        il.Emit(OpCodes.Ret);
        return generated;
    }
}
