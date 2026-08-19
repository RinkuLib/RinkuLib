using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Querying.Parameters;

internal sealed class DictionaryParameterAccess {
    private readonly ParameterMemberAccess? _container;
    private readonly DictionaryShape _shape;

    internal DictionaryParameterAccess(Type rootType, ParameterMemberAccess? container, DictionaryShape shape, string key, string? fallbackKey, int depth) {
        RootType = rootType;
        _container = container;
        _shape = shape;
        Key = key;
        FallbackKey = fallbackKey;
        Depth = depth;
    }

    internal Type RootType { get; }
    internal string Key { get; }
    internal string? FallbackKey { get; }
    internal int Depth { get; }
    internal Type ValueType => _shape.ValueType;
    internal bool IsNested => _container is not null;

    internal LocalBuilder EmitTryGet(ILGenerator il, Label missing, int sourceArgument = 0) {
        if (_shape.TryGetValue is not null)
            return EmitGenericTryGet(il, missing, sourceArgument);
        return EmitNonGenericTryGet(il, missing, sourceArgument);
    }

    private LocalBuilder EmitGenericTryGet(ILGenerator il, Label missing, int sourceArgument) {
        LocalBuilder dictionary = il.DeclareLocal(_shape.InterfaceType);
        EmitDictionary(il, missing, sourceArgument);
        il.Emit(OpCodes.Castclass, _shape.InterfaceType);
        il.Emit(OpCodes.Stloc, dictionary);

        LocalBuilder value = il.DeclareLocal(_shape.ValueType);
        Label found = il.DefineLabel();
        EmitGenericTryGet(il, dictionary, value, Key);
        il.Emit(OpCodes.Brtrue, found);
        if (FallbackKey is null) {
            il.Emit(OpCodes.Br, missing);
        } else {
            EmitGenericTryGet(il, dictionary, value, FallbackKey);
            il.Emit(OpCodes.Brfalse, missing);
        }
        il.MarkLabel(found);
        return value;
    }

    private void EmitGenericTryGet(ILGenerator il, LocalBuilder dictionary, LocalBuilder value, string key) {
        il.Emit(OpCodes.Ldloc, dictionary);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _shape.TryGetValue!);
    }

    private LocalBuilder EmitNonGenericTryGet(ILGenerator il, Label missing, int sourceArgument) {
        LocalBuilder dictionary = il.DeclareLocal(typeof(IDictionary));
        EmitDictionary(il, missing, sourceArgument);
        il.Emit(OpCodes.Castclass, typeof(IDictionary));
        il.Emit(OpCodes.Stloc, dictionary);

        LocalBuilder value = il.DeclareLocal(typeof(object));
        Label found = il.DefineLabel();
        EmitNonGenericTryGet(il, dictionary, value, Key, found);
        if (FallbackKey is null) {
            il.Emit(OpCodes.Br, missing);
        } else {
            EmitNonGenericTryGet(il, dictionary, value, FallbackKey, found);
            il.Emit(OpCodes.Br, missing);
        }
        il.MarkLabel(found);
        return value;
    }

    private static void EmitNonGenericTryGet(ILGenerator il, LocalBuilder dictionary, LocalBuilder value, string key, Label found) {
        Label next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictionary);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Callvirt, typeof(IDictionary).GetMethod(nameof(IDictionary.Contains))!);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, dictionary);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Callvirt, typeof(IDictionary).GetProperty("Item")!.GetMethod!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, found);
        il.MarkLabel(next);
    }

    private void EmitDictionary(ILGenerator il, Label missing, int sourceArgument) {
        if (_container is null) {
            if (RootType.IsValueType) {
                AccessorEmitter.EmitSourceLoad(il, sourceArgument);
                il.Emit(OpCodes.Ldobj, RootType);
                il.Emit(OpCodes.Box, RootType);
            }
            else {
                AccessorEmitter.EmitSourceLoad(il, sourceArgument);
            }
            return;
        }

        ParameterMemberAccess prepared = _container.Value.Prepare(il, missing, sourceArgument);
        Type containerType = prepared.MemberType;
        prepared.EmitLoad(il);

        if (!containerType.IsValueType) {
            LocalBuilder value = il.DeclareLocal(containerType);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldloc, value);
            return;
        }

        Type? nullableType = Nullable.GetUnderlyingType(containerType);
        if (nullableType is not null) {
            LocalBuilder nullable = il.DeclareLocal(containerType);
            il.Emit(OpCodes.Stloc, nullable);
            il.Emit(OpCodes.Ldloca, nullable);
            il.Emit(OpCodes.Call, containerType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldloca, nullable);
            il.Emit(OpCodes.Call, containerType.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
            il.Emit(OpCodes.Box, nullableType);
            return;
        }

        il.Emit(OpCodes.Box, containerType);
    }

    internal static bool EmitDefaultUsage(ILGenerator il, LocalBuilder value, Type valueType) {
        if (!valueType.IsValueType) {
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return true;
        }
        if (Nullable.GetUnderlyingType(valueType) is not null) {
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, valueType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            return true;
        }
        il.Emit(OpCodes.Ldc_I4_1);
        return true;
    }

    internal static void EmitBoxedValue(ILGenerator il, LocalBuilder value, Type valueType) {
        il.Emit(OpCodes.Ldloc, value);
        if (valueType.IsValueType)
            il.Emit(OpCodes.Box, valueType);
    }

    internal sealed class DictionaryShape {
        internal Type InterfaceType { get; }
        internal Type ValueType { get; }
        internal MethodInfo? TryGetValue { get; }

        private DictionaryShape(Type interfaceType, Type valueType, MethodInfo? tryGetValue) {
            InterfaceType = interfaceType;
            ValueType = valueType;
            TryGetValue = tryGetValue;
        }

        internal static bool TryCreate(Type type, out DictionaryShape? shape) {
            Type? generic = FindGenericDictionary(type, typeof(IReadOnlyDictionary<,>))
                ?? FindGenericDictionary(type, typeof(IDictionary<,>));
            if (generic is not null) {
                Type valueType = generic.GetGenericArguments()[1];
                shape = new DictionaryShape(generic, valueType,
                    generic.GetMethod("TryGetValue", [typeof(string), valueType.MakeByRefType()])!);
                return true;
            }
            if (typeof(IDictionary).IsAssignableFrom(type)) {
                shape = new DictionaryShape(typeof(IDictionary), typeof(object), null);
                return true;
            }
            shape = null;
            return false;
        }

        private static Type? FindGenericDictionary(Type type, Type definition) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == definition
                && type.GetGenericArguments()[0] == typeof(string))
                return type;
            foreach (Type iface in type.GetInterfaces())
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == definition
                    && iface.GetGenericArguments()[0] == typeof(string))
                    return iface;
            return null;
        }
    }
}
