using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

// Generation-time union for delegate-backed behavior and directly-emittable methods.
internal sealed class RuntimeCall<TDelegate> where TDelegate : Delegate {
    private readonly TDelegate? _delegate;
    private readonly TDelegate? _fallback;
    private readonly MethodInfo? _method;
    private readonly object? _target;
    private readonly bool _direct;

    public RuntimeCall(TDelegate handler) => _delegate = handler ?? throw new ArgumentNullException(nameof(handler));

    public RuntimeCall(MethodInfo method, object? target = null) {
        ArgumentNullException.ThrowIfNull(method);
        if (method.ContainsGenericParameters) throw new ArgumentException("Open generic methods cannot be emitted as runtime behavior.", nameof(method));
        if (method.IsStatic && target is not null) throw new ArgumentException("A static runtime method cannot have a target instance.", nameof(target));

        _method = method;
        _target = target;
        try {
            _fallback = (TDelegate)(target is null
                ? method.CreateDelegate(typeof(TDelegate))
                : method.CreateDelegate(typeof(TDelegate), target));
        }
        catch (Exception ex) {
            throw new ArgumentException($"Method {method} does not match delegate contract {typeof(TDelegate)}.", nameof(method), ex);
        }

        Type? declaring = method.DeclaringType;
        _direct = method.IsPublic && declaring is not null && declaring.IsVisible && (!declaring.IsValueType || method.IsStatic);
    }

    public void Emit(RuntimeTrackingCapabilityBuilder builder, ILGenerator il, Action<ILGenerator> emitArguments, string fieldName) {
        if (_method is not null && _direct) {
            if (!_method.IsStatic && _target is not null) {
                FieldBuilder target = builder.DefineStaticField(_method.DeclaringType!, _target, fieldName + "Target");
                il.Emit(OpCodes.Ldsfld, target);
            }
            emitArguments(il);
            il.Emit(_method.IsStatic || !_method.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, _method);
            return;
        }

        TDelegate handler = _delegate ?? _fallback ?? throw new InvalidOperationException("Runtime call is not initialized.");
        FieldBuilder field = builder.DefineStaticField(typeof(TDelegate), handler, fieldName);
        il.Emit(OpCodes.Ldsfld, field);
        emitArguments(il);
        il.Emit(OpCodes.Callvirt, typeof(TDelegate).GetMethod("Invoke")!);
    }
}
