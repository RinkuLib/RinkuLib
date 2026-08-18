using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Contributes behavior to a generated tracking type.</summary>
public interface IRuntimeTrackingCapability<TOriginal> {
    /// <summary>Emits the capability behavior.</summary>
    void Emit(RuntimeTrackingCapabilityBuilder builder);
}

/// <summary>Builds capability members for a generated tracking type.</summary>
public sealed class RuntimeTrackingCapabilityBuilder {
    private readonly TypeBuilder _type;
    private readonly Dictionary<Type, byte> _interfaces = [];
    private readonly Dictionary<string, FieldBuilder> _instanceFields = [];
    private readonly Dictionary<MethodInfo, MethodBuilder> _methods = [];
    private readonly HashSet<MethodInfo> _implemented = [];
    private readonly List<(string Field, object? Value)> _staticValues = [];
    private readonly List<(string Field, Func<Type, object?> Factory)> _staticFactories = [];
    private int _fieldId;
    private int _methodId;

    internal RuntimeTrackingCapabilityBuilder(TypeBuilder type, Type exposedContract, FieldBuilder originalField, FieldBuilder editField, FieldBuilder? propertyChangedField) {
        _type = type;
        ExposedContract = exposedContract;
        OriginalField = originalField;
        EditField = editField;
        PropertyChangedField = propertyChangedField;
    }

    /// <summary>Gets the generated type builder.</summary>
    public TypeBuilder TypeBuilder => _type;
    /// <summary>Gets the exposed contract type.</summary>
    public Type ExposedContract { get; }
    /// <summary>Gets the original-value field.</summary>
    public FieldBuilder OriginalField { get; }
    /// <summary>Gets the edit-state field.</summary>
    public FieldBuilder EditField { get; }
    /// <summary>Gets the notification field when enabled.</summary>
    public FieldBuilder? PropertyChangedField { get; }
    /// <summary>Gets whether notifications are enabled.</summary>
    public bool NotificationsEnabled => PropertyChangedField is not null;

    /// <summary>Adds an interface to the generated type.</summary>
    public void AddInterface(Type interfaceType) {
        ArgumentNullException.ThrowIfNull(interfaceType);
        if (!interfaceType.IsInterface) throw new ArgumentException($"{interfaceType} is not an interface.", nameof(interfaceType));
        if (!interfaceType.IsVisible) throw new NotSupportedException($"Generated capability interface {interfaceType} must be publicly visible.");
        if (!_interfaces.TryAdd(interfaceType, 0)) return;

        // Do not repeat inherited interfaces in the generated type metadata. This is common for strong
        // TEdit contracts and validation bundles and has no behavioral value.
        if (interfaceType != ExposedContract && interfaceType.IsAssignableFrom(ExposedContract)) return;
        foreach (Type implemented in _interfaces.Keys)
            if (implemented != interfaceType && interfaceType.IsAssignableFrom(implemented)) return;
        _type.AddInterfaceImplementation(interfaceType);
    }


    /// <summary>Emits a property-change notification.</summary>
    public void EmitRaiseChanged(ILGenerator il, string? propertyName)
        => EmitRaiseChanged(il, propertyName, static emit => emit.Emit(OpCodes.Ldarg_0));

    /// <summary>Emits a property-change notification with a custom item load.</summary>
    public void EmitRaiseChanged(ILGenerator il, string? propertyName, Action<ILGenerator> emitItem) {
        ArgumentNullException.ThrowIfNull(emitItem);
        if (PropertyChangedField is not FieldBuilder field) return;
        emitItem(il);
        il.Emit(OpCodes.Ldfld, field);
        emitItem(il);
        if (propertyName is null) il.Emit(OpCodes.Ldnull);
        else il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, typeof(RuntimePropertyChangedHub).GetMethod(nameof(RuntimePropertyChangedHub.Raise))!);
    }

    /// <summary>Gets or creates an instance field.</summary>
    public FieldBuilder GetOrAddInstanceField(string key, Type fieldType, string? namePrefix = null) {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(fieldType);
        if (_instanceFields.TryGetValue(key, out FieldBuilder? field)) {
            if (field.FieldType != fieldType)
                throw new InvalidOperationException($"Capability field '{key}' was already defined as {field.FieldType}, not {fieldType}.");
            return field;
        }
        if (fieldType.IsByRef || fieldType.IsPointer || fieldType.IsByRefLike || fieldType.ContainsGenericParameters)
            throw new NotSupportedException($"Generated capability state cannot store {fieldType}.");
        field = _type.DefineField($"_{namePrefix ?? "capability"}{_fieldId++}", fieldType, FieldAttributes.Private);
        _instanceFields.Add(key, field);
        return field;
    }

    /// <summary>Defines an initialized static field.</summary>
    public FieldBuilder DefineStaticField(Type fieldType, object? value, string? namePrefix = null) {
        ArgumentNullException.ThrowIfNull(fieldType);
        FieldBuilder field = _type.DefineField($"s_{namePrefix ?? "capability"}{_fieldId++}", fieldType, FieldAttributes.Private | FieldAttributes.Static);
        _staticValues.Add((field.Name, value));
        return field;
    }

    /// <summary>Defines a static field initialized by a factory.</summary>
    public FieldBuilder DefineInitializedStaticField(Type fieldType, Func<Type, object?> factory, string? namePrefix = null) {
        ArgumentNullException.ThrowIfNull(fieldType);
        ArgumentNullException.ThrowIfNull(factory);
        FieldBuilder field = _type.DefineField($"s_{namePrefix ?? "capability"}{_fieldId++}", fieldType, FieldAttributes.Private | FieldAttributes.Static);
        _staticFactories.Add((field.Name, factory));
        return field;
    }

    /// <summary>Gets whether a contract method is implemented.</summary>
    public bool IsImplemented(MethodInfo contract) => _implemented.Contains(contract) || _methods.ContainsKey(contract);
    /// <summary>Gets whether a contract method supplies its own behavior.</summary>
    public bool HasDefaultImplementation(MethodInfo contract) => RuntimeInterfaceDefaults.HasImplementation(ExposedContract, contract);

    internal void MarkImplemented(MethodInfo contract) {
        ArgumentNullException.ThrowIfNull(contract);
        _implemented.Add(contract);
    }

    /// <summary>Defines a static helper method.</summary>
    public MethodBuilder DefineStaticMethod(string namePrefix, Type returnType, params Type[] parameterTypes) {
        ArgumentException.ThrowIfNullOrEmpty(namePrefix);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(parameterTypes);
        return _type.DefineMethod($"__{namePrefix}{_methodId++}",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            returnType, parameterTypes);
    }

    /// <summary>Implements a contract method.</summary>
    public MethodBuilder Implement(MethodInfo contract, Action<ILGenerator> emit, bool reuseExisting = false) {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(emit);
        if (_methods.TryGetValue(contract, out MethodBuilder? existing)) {
            if (reuseExisting) return existing;
            throw new InvalidOperationException($"Generated capability method {contract} is already implemented.");
        }

        MethodAttributes attributes = MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot;
        if (contract.IsSpecialName) attributes |= MethodAttributes.SpecialName;

        MethodBuilder method = _type.DefineMethod($"__cap{_methodId++}_{contract.Name}", attributes,
            contract.ReturnType, contract.GetParameters().Select(static x => x.ParameterType).ToArray());
        emit(method.GetILGenerator());
        _type.DefineMethodOverride(method, contract);
        _methods.Add(contract, method);
        _implemented.Add(contract);
        return method;
    }

    internal void Initialize(Type generatedType) {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach ((string field, object? value) in _staticValues)
            generatedType.GetField(field, flags)!.SetValue(null, value);
        foreach ((string field, Func<Type, object?> factory) in _staticFactories)
            generatedType.GetField(field, flags)!.SetValue(null, factory(generatedType));
    }
}
