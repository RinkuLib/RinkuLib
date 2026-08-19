using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal interface IRuntimeOriginalReader {
    Type ValueType { get; }
    void EmitRead(ILGenerator il, Action<ILGenerator> emitOriginal);
}

internal interface IRuntimeOriginalWriter {
    Type ValueType { get; }
    void EmitWrite(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue);
}

internal sealed class PropertyOriginalReader(PropertyInfo property) : IRuntimeOriginalReader {
    private readonly MethodInfo _getter = property.GetMethod ?? throw new ArgumentException($"{property} has no getter.", nameof(property));
    public Type ValueType => property.PropertyType;
    public void EmitRead(ILGenerator il, Action<ILGenerator> emitOriginal) {
        emitOriginal(il);
        il.Emit(property.DeclaringType!.IsValueType ? OpCodes.Call : OpCodes.Callvirt, _getter);
    }
}

internal sealed class PropertyOriginalWriter(PropertyInfo property) : IRuntimeOriginalWriter {
    private readonly MethodInfo _setter = property.SetMethod ?? throw new ArgumentException($"{property} has no setter.", nameof(property));
    public Type ValueType => property.PropertyType;
    public void EmitWrite(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue) {
        emitOriginal(il);
        emitValue(il);
        il.Emit(property.DeclaringType!.IsValueType ? OpCodes.Call : OpCodes.Callvirt, _setter);
    }
}

internal sealed class FieldOriginalReader(FieldInfo fieldInfo) : IRuntimeOriginalReader {
    public Type ValueType => fieldInfo.FieldType;
    public void EmitRead(ILGenerator il, Action<ILGenerator> emitOriginal) {
        emitOriginal(il);
        il.Emit(OpCodes.Ldfld, fieldInfo);
    }
}

internal sealed class FieldOriginalWriter(FieldInfo fieldInfo) : IRuntimeOriginalWriter {
    public Type ValueType => fieldInfo.FieldType;
    public void EmitWrite(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue) {
        emitOriginal(il);
        emitValue(il);
        il.Emit(OpCodes.Stfld, fieldInfo);
    }
}

internal sealed class MethodOriginalReader(Type originalType, MethodInfo method, Type valueType) : IRuntimeOriginalReader {
    public Type ValueType => valueType;

    public void EmitRead(ILGenerator il, Action<ILGenerator> emitOriginal) {
        if (method.IsStatic) {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != originalType)
                throw new NotSupportedException($"Static source getter {method} must have signature {valueType.Name} M({originalType.Name}).");
            emitOriginal(il);
            if (originalType.IsValueType) il.Emit(OpCodes.Ldobj, originalType);
            il.Emit(OpCodes.Call, method);
            return;
        }
        emitOriginal(il);
        il.Emit(originalType.IsValueType || !method.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, method);
    }
}

internal sealed class MethodOriginalWriter(Type originalType, MethodInfo method, Type valueType) : IRuntimeOriginalWriter {
    public Type ValueType => valueType;

    public void EmitWrite(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue) {
        if (method.IsStatic) {
            if (originalType.IsValueType)
                throw new NotSupportedException($"Static by-value setter {method} cannot mutate value-type original {originalType}; use an instance method or custom member emitter.");
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != originalType || parameters[1].ParameterType != valueType)
                throw new NotSupportedException($"Static source setter {method} must have signature void M({originalType.Name}, {valueType.Name}).");
            emitOriginal(il);
            emitValue(il);
            il.Emit(OpCodes.Call, method);
            return;
        }
        emitOriginal(il);
        emitValue(il);
        il.Emit(originalType.IsValueType || !method.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, method);
    }
}
