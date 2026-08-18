using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal sealed class RuntimeNewOriginalCall<TOriginal> {
    private readonly RuntimeCall<Func<TOriginal>>? _call;
    private readonly ConstructorInfo? _constructor;
    private readonly bool _defaultValue;

    public RuntimeNewOriginalCall(Func<TOriginal> factory) => _call = new(factory);
    public RuntimeNewOriginalCall(MethodInfo method, object? target = null) => _call = new(method, target);

    public RuntimeNewOriginalCall(ConstructorInfo constructor) {
        ArgumentNullException.ThrowIfNull(constructor);
        Type type = constructor.DeclaringType ?? throw new ArgumentException("Constructor has no declaring type.", nameof(constructor));
        if (constructor.GetParameters().Length != 0 || !typeof(TOriginal).IsAssignableFrom(type))
            throw new ArgumentException($"Constructor {constructor} must be parameterless and construct a {typeof(TOriginal)}.", nameof(constructor));
        if (constructor.IsPublic && type.IsVisible) _constructor = constructor;
        else _call = new(BuildFactory(constructor));
    }

    private RuntimeNewOriginalCall(bool defaultValue) => _defaultValue = defaultValue;

    public static RuntimeNewOriginalCall<TOriginal> Default() {
        if (typeof(TOriginal).IsValueType) return new(true);
        ConstructorInfo ctor = typeof(TOriginal).GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException($"{typeof(TOriginal)} has no public parameterless constructor.");
        return new(ctor);
    }

    public void Emit(RuntimeTrackingCapabilityBuilder builder, ILGenerator il) {
        if (_defaultValue) {
            LocalBuilder value = il.DeclareLocal(typeof(TOriginal));
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Initobj, typeof(TOriginal));
            il.Emit(OpCodes.Ldloc, value);
            return;
        }
        if (_constructor is not null) {
            il.Emit(OpCodes.Newobj, _constructor);
            EmitConversion(il, _constructor.DeclaringType!);
            return;
        }
        (_call ?? throw new InvalidOperationException()).Emit(builder, il, static _ => { }, "newOriginal");
    }

    private static Func<TOriginal> BuildFactory(ConstructorInfo constructor) {
        Type type = constructor.DeclaringType!;
        var dm = new DynamicMethod($"TrackingNewOriginal_{type.Name}", typeof(TOriginal), Type.EmptyTypes,
            typeof(RuntimeNewOriginalCall<TOriginal>).Module, true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        EmitConversion(il, type);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Func<TOriginal>>();
    }

    private static void EmitConversion(ILGenerator il, Type createdType) {
        if (createdType == typeof(TOriginal)) return;
        if (createdType.IsValueType) il.Emit(OpCodes.Box, createdType);
        il.Emit(OpCodes.Castclass, typeof(TOriginal));
    }
}
