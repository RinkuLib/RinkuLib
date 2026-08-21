using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Binding;

internal sealed class RuntimeBindingOption<TOriginal> : IRuntimeTrackingOption<TOriginal>
{
    internal static readonly RuntimeBindingOption<TOriginal> Instance = new();
    private RuntimeBindingOption() { }

    public void Apply(RuntimeTrackingTypeDefinition<TOriginal> type)
    {
        type.RequireInterface(typeof(INotifyPropertyChanged));
        type.RequireInterface(typeof(IEditableObject));
        for (int i = 0; i < type.TypeEmitters.Count; i++)
            if (type.TypeEmitters[i] is RuntimeBindingEmitter<TOriginal>) return;
        type.AddTypeEmitter(new RuntimeBindingEmitter<TOriginal>());
    }
}

internal sealed class RuntimeBindingEmitter<TOriginal> : IRuntimeTrackingTypeEmitter<TOriginal>
{
    public void Emit(RuntimeTrackingEmitContext<TOriginal> context)
    {
        EmitNotifications(context);
        EmitEditableObject(context);
    }

    private static void EmitNotifications(RuntimeTrackingEmitContext<TOriginal> context)
    {
        FieldBuilder field = context.TypeBuilder.DefineField("_propertyChanged", typeof(PropertyChangedEventHandler), FieldAttributes.Private);
        MethodInfo raise = typeof(PropertyChangedHub).GetMethod(nameof(PropertyChangedHub.Raise))
            ?? throw new MissingMethodException(typeof(PropertyChangedHub).FullName, nameof(PropertyChangedHub.Raise));
        context.SetChangedEmitter((il, propertyName) =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_0);
            if (propertyName is null) il.Emit(OpCodes.Ldnull);
            else il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Call, raise);
        });

        EventInfo contractEvent = typeof(INotifyPropertyChanged).GetEvent(nameof(INotifyPropertyChanged.PropertyChanged))
            ?? throw new MissingMemberException(typeof(INotifyPropertyChanged).FullName, nameof(INotifyPropertyChanged.PropertyChanged));
        MethodInfo addContract = contractEvent.AddMethod
            ?? throw new MissingMethodException(typeof(INotifyPropertyChanged).FullName, "add_PropertyChanged");
        MethodInfo removeContract = contractEvent.RemoveMethod
            ?? throw new MissingMethodException(typeof(INotifyPropertyChanged).FullName, "remove_PropertyChanged");
        EventBuilder generatedEvent = context.TypeBuilder.DefineEvent(nameof(INotifyPropertyChanged.PropertyChanged), EventAttributes.None, typeof(PropertyChangedEventHandler));

        MethodBuilder add = DefineExplicit(context.TypeBuilder, addContract);
        ILGenerator addIl = add.GetILGenerator();
        addIl.Emit(OpCodes.Ldarg_0);
        addIl.Emit(OpCodes.Ldflda, field);
        addIl.Emit(OpCodes.Ldarg_1);
        addIl.Emit(OpCodes.Call, typeof(PropertyChangedHub).GetMethod(nameof(PropertyChangedHub.Add))
            ?? throw new MissingMethodException(typeof(PropertyChangedHub).FullName, nameof(PropertyChangedHub.Add)));
        addIl.Emit(OpCodes.Ret);

        MethodBuilder remove = DefineExplicit(context.TypeBuilder, removeContract);
        ILGenerator removeIl = remove.GetILGenerator();
        removeIl.Emit(OpCodes.Ldarg_0);
        removeIl.Emit(OpCodes.Ldflda, field);
        removeIl.Emit(OpCodes.Ldarg_1);
        removeIl.Emit(OpCodes.Call, typeof(PropertyChangedHub).GetMethod(nameof(PropertyChangedHub.Remove))
            ?? throw new MissingMethodException(typeof(PropertyChangedHub).FullName, nameof(PropertyChangedHub.Remove)));
        removeIl.Emit(OpCodes.Ret);

        generatedEvent.SetAddOnMethod(add);
        generatedEvent.SetRemoveOnMethod(remove);
        context.TypeBuilder.DefineMethodOverride(add, addContract);
        context.TypeBuilder.DefineMethodOverride(remove, removeContract);
    }

    private static void EmitEditableObject(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo beginContract = typeof(IEditableObject).GetMethod(nameof(IEditableObject.BeginEdit))
            ?? throw new MissingMethodException(typeof(IEditableObject).FullName, nameof(IEditableObject.BeginEdit));
        MethodInfo cancelContract = typeof(IEditableObject).GetMethod(nameof(IEditableObject.CancelEdit))
            ?? throw new MissingMethodException(typeof(IEditableObject).FullName, nameof(IEditableObject.CancelEdit));
        MethodInfo endContract = typeof(IEditableObject).GetMethod(nameof(IEditableObject.EndEdit))
            ?? throw new MissingMethodException(typeof(IEditableObject).FullName, nameof(IEditableObject.EndEdit));
        MethodInfo ensure = typeof(IEditable).GetMethod(nameof(IEditable.EnsureEditing))
            ?? throw new MissingMethodException(typeof(IEditable).FullName, nameof(IEditable.EnsureEditing));
        MethodInfo cancel = typeof(IEditable).GetMethod(nameof(IEditable.CancelEdit))
            ?? throw new MissingMethodException(typeof(IEditable).FullName, nameof(IEditable.CancelEdit));

        MethodBuilder begin = DefineExplicit(context.TypeBuilder, beginContract);
        ILGenerator beginIl = begin.GetILGenerator();
        beginIl.Emit(OpCodes.Ldarg_0);
        beginIl.Emit(OpCodes.Callvirt, ensure);
        beginIl.Emit(OpCodes.Pop);
        beginIl.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(begin, beginContract);

        MethodBuilder cancelEdit = DefineExplicit(context.TypeBuilder, cancelContract);
        ILGenerator cancelIl = cancelEdit.GetILGenerator();
        cancelIl.Emit(OpCodes.Ldarg_0);
        cancelIl.Emit(OpCodes.Callvirt, cancel);
        cancelIl.Emit(OpCodes.Pop);
        cancelIl.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(cancelEdit, cancelContract);

        MethodBuilder end = DefineExplicit(context.TypeBuilder, endContract);
        end.GetILGenerator().Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(end, endContract);
    }

    private static MethodBuilder DefineExplicit(TypeBuilder type, MethodInfo contract)
        => type.DefineMethod(
            $"__binding_{contract.Name}",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            contract.ReturnType,
            contract.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());
}

/// <summary>Provides binding options for generated tracking types.</summary>
public static class RuntimeBindingOptionsExtensions
{
    /// <summary>Adds component model binding behavior.</summary>
    public static RuntimeTrackingOptions<TOriginal> Binding<TOriginal>(this RuntimeTrackingOptions<TOriginal> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Add(RuntimeBindingOption<TOriginal>.Instance);
    }
}
