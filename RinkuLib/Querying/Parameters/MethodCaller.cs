using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;
using Rinku.Querying.Parameters;

namespace Rinku;

/// <summary>
/// Describes one value that is supplied directly by the generated caller instead of being resolved
/// from the caller's first (mapped) argument.
/// </summary>
public abstract class CallerParameter {
    private protected CallerParameter(Type valueType, string? targetName) {
        ValueType = valueType;
        TargetName = targetName;
    }

    /// <summary>The delegate argument type supplied by the caller.</summary>
    public Type ValueType { get; }

    /// <summary>
    /// The target method parameter name, or <see langword="null"/> when the target is matched by exact type.
    /// </summary>
    public string? TargetName { get; }
}

/// <summary>Describes a caller-supplied argument of type <typeparamref name="T"/>.</summary>
public sealed class CallerParameter<T> : CallerParameter {
    private static readonly CallerParameter<T> TypeOnly = new(targetName: null);

    private CallerParameter(string? targetName) : base(typeof(T), targetName) { }

    /// <summary>
    /// Matches one otherwise-unbound target parameter whose type is exactly <typeparamref name="T"/>.
    /// No match is allowed. The caller argument is simply ignored when absent. Multiple matches are ambiguous and throw.
    /// </summary>
    public static CallerParameter<T> ByType() => TypeOnly;

    /// <summary>
    /// Supplies the named target parameter directly. When the target method does not contain that parameter,
    /// the caller argument is simply ignored so the same delegate shape can be reused across compatible methods.
    /// </summary>
    public static CallerParameter<T> Named(string parameterName)
        => new(string.IsNullOrWhiteSpace(parameterName)
            ? throw new ArgumentException("A caller parameter name cannot be empty.", nameof(parameterName))
            : parameterName);
}

/// <summary>
/// Generates strongly typed delegates that call a method using Rinku's parameter-shape rules for the first
/// delegate argument. Additional delegate arguments are passed directly to selected target parameters.
/// </summary>
/// <remarks>
/// The generated delegate is monomorphic. Mapping is built against the delegate's first parameter type exactly.
/// It does not choose a different mapping after creation.
/// </remarks>
public static class MethodCaller {
    /// <summary>
    /// Generates a caller. The first delegate argument is the mapped source.
    /// Every later argument is caller-supplied. Without <paramref name="callerParameters"/>, caller-supplied arguments match target parameters by exact type.
    /// </summary>
    /// <typeparam name="TDelegate">The exact delegate signature to generate, including the method return type.</typeparam>
    /// <param name="method">The static method to call, or an instance method implemented by the mapped source itself.</param>
    /// <param name="callerParameters">
    /// Optional positional configuration for the delegate arguments after the mapped source. Supply either none
    /// (all match by exact type), or one entry for every caller-supplied delegate argument.
    /// </param>
    public static TDelegate Create<TDelegate>(MethodInfo method, params CallerParameter[] callerParameters)
        where TDelegate : Delegate {
        ArgumentNullException.ThrowIfNull(method);
        callerParameters ??= [];

        MethodInfo invoke = typeof(TDelegate).GetMethod(nameof(Action.Invoke))
            ?? throw new ArgumentException($"{typeof(TDelegate)} is not an invokable delegate type.", nameof(TDelegate));
        ParameterInfo[] delegateParameters = invoke.GetParameters();
        if (delegateParameters.Length == 0)
            throw new NotSupportedException("A generated method caller requires a mapped source as its first delegate argument.");
        if (delegateParameters.Any(static p => p.ParameterType.IsByRef))
            throw new NotSupportedException("ref/in/out parameters are not supported by the generated caller yet.");
        if (method.ContainsGenericParameters)
            throw new NotSupportedException("Open generic methods cannot be used by a generated caller.");
        ParameterInfo[] targetParameters = method.GetParameters();
        if (targetParameters.Any(static p => p.ParameterType.IsByRef))
            throw new NotSupportedException("Target ref/in/out parameters are not supported by the generated caller yet.");

        Type sourceType = delegateParameters[0].ParameterType;
        if (sourceType.IsValueType)
            throw new NotSupportedException("Value-type mapped sources are not supported by MethodCaller yet. Use a reference/interface source.");

        int callerCount = delegateParameters.Length - 1;
        if (callerParameters.Length != 0 && callerParameters.Length != callerCount)
            throw new ArgumentException($"The delegate has {callerCount} caller-supplied argument(s), but {callerParameters.Length} caller parameter configuration(s) were supplied.", nameof(callerParameters));

        ValidateReturn(method.ReturnType, invoke.ReturnType);
        ValidateInstanceTarget(method, sourceType);

        var callerBindings = ResolveCallerBindings(delegateParameters, targetParameters, callerParameters);
        var mappedNames = new List<string>(targetParameters.Length);
        var bindings = new MethodArgumentBinding[targetParameters.Length];
        for (int i = 0; i < targetParameters.Length; i++) {
            if (callerBindings[i] >= 0) {
                bindings[i] = MethodArgumentBinding.Caller(callerBindings[i]);
                continue;
            }
            string name = targetParameters[i].Name
                ?? throw new NotSupportedException($"Target parameter #{i} on '{method}' does not expose a name.");
            int mapperIndex = mappedNames.Count;
            mappedNames.Add(name);
            bindings[i] = MethodArgumentBinding.Mapped(mapperIndex);
        }

        Mapper? mapper = mappedNames.Count == 0 ? null : Mapper.GetMapper(mappedNames.ToArray());
        try {
            ParameterAccessorGenerator.StackAccessor? stackAccessor = mapper is null
                ? null
                : ParameterAccessorGenerator.CreateStack(sourceType, mapper);

            if (stackAccessor is not null)
                ValidateMappedBindings(stackAccessor, targetParameters, bindings, sourceType, method);

            var dm = new DynamicMethod($"Call_{method.DeclaringType?.Name}_{method.Name}_{typeof(TDelegate).Name}",
                invoke.ReturnType, delegateParameters.Select(static p => p.ParameterType).ToArray(),
                typeof(MethodCaller).Module, skipVisibility: true);
            ILGenerator il = dm.GetILGenerator();
            ParameterAccessorGenerator.StackAccessorEmission? emission = stackAccessor?.Begin(il);

            // A mapped parameter must be usable before any method arguments are placed on the evaluation stack.
            // Caller-supplied parameters intentionally bypass mapping/usage conditions.
            if (emission is not null) {
                for (int i = 0; i < bindings.Length; i++) {
                    if (!bindings[i].IsMapped) continue;
                    int slot = bindings[i].Index;
                    emission.EmitUsage(slot);
                    Label available = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue, available);
                    il.Emit(OpCodes.Ldstr, targetParameters[i].Name ?? $"#{i}");
                    il.Emit(OpCodes.Call, MissingParameterExceptionMethod);
                    il.Emit(OpCodes.Throw);
                    il.MarkLabel(available);
                }
            }

            if (!method.IsStatic)
                il.Emit(OpCodes.Ldarg_0); // instance method implemented by the mapped source itself

            for (int i = 0; i < bindings.Length; i++) {
                MethodArgumentBinding binding = bindings[i];
                Type targetType = targetParameters[i].ParameterType;
                if (binding.IsMapped) {
                    emission!.EmitValue(binding.Index);
                    Type sourceValueType = emission.GetValueType(binding.Index)
                        ?? throw MissingMappedSource(sourceType, targetParameters[i], method);
                    EmitConversion(il, sourceValueType, targetType, $"mapped parameter '{targetParameters[i].Name}'");
                }
                else {
                    int delegateArgumentIndex = binding.Index + 1; // source is delegate argument 0
                    Type callerType = delegateParameters[delegateArgumentIndex].ParameterType;
                    il.Emit(OpCodes.Ldarg, delegateArgumentIndex);
                    EmitConversion(il, callerType, targetType, $"caller parameter '{targetParameters[i].Name}'");
                }
            }

            il.Emit(method.IsStatic || !method.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, method);
            EmitConversion(il, method.ReturnType, invoke.ReturnType, "return value");
            il.Emit(OpCodes.Ret);
            return dm.CreateDelegate<TDelegate>();
        }
        finally {
            mapper?.Dispose();
        }
    }

    private static readonly MethodInfo MissingParameterExceptionMethod = typeof(MethodCaller)
        .GetMethod(nameof(CreateMissingParameterException), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Exception CreateMissingParameterException(string name)
        => new InvalidOperationException($"Mapped method parameter '{name}' is not currently usable from the supplied source.");

    private static int[] ResolveCallerBindings(ParameterInfo[] delegateParameters, ParameterInfo[] targetParameters, CallerParameter[] configurations) {
        var targetBindings = new int[targetParameters.Length];
        Array.Fill(targetBindings, -1);
        int callerCount = delegateParameters.Length - 1;
        if (callerCount == 0) return targetBindings;

        CallerParameter?[] effective = new CallerParameter?[callerCount];
        if (configurations.Length != 0) {
            for (int i = 0; i < callerCount; i++) {
                CallerParameter config = configurations[i]
                    ?? throw new ArgumentNullException(nameof(configurations), $"Caller parameter configuration #{i} is null.");
                Type delegateType = delegateParameters[i + 1].ParameterType;
                if (config.ValueType != delegateType)
                    throw new ArgumentException($"Caller configuration #{i} describes {config.ValueType}, but delegate argument #{i + 1} is {delegateType}.", nameof(configurations));
                effective[i] = config;
            }
        }

        // Named bindings first: a precise request has precedence over type-only matching.
        for (int callerIndex = 0; callerIndex < callerCount; callerIndex++) {
            CallerParameter? config = effective[callerIndex];
            if (config?.TargetName is null) continue;
            int targetIndex = FindNamedTarget(targetParameters, config.TargetName);
            if (targetIndex < 0) continue; // intentionally optional; common caller conventions may be unused
            if (targetBindings[targetIndex] >= 0)
                throw new InvalidOperationException($"Target parameter '{targetParameters[targetIndex].Name}' is supplied by more than one caller argument.");
            Type callerType = delegateParameters[callerIndex + 1].ParameterType;
            EnsureConvertible(callerType, targetParameters[targetIndex].ParameterType,
                $"caller argument #{callerIndex + 1} for '{targetParameters[targetIndex].Name}'");
            targetBindings[targetIndex] = callerIndex;
        }

        // Unconfigured or ByType caller arguments use exact-type matching only. Zero matches means the argument is unused.
        for (int callerIndex = 0; callerIndex < callerCount; callerIndex++) {
            CallerParameter? config = effective[callerIndex];
            if (config?.TargetName is not null) continue;
            Type callerType = delegateParameters[callerIndex + 1].ParameterType;
            int match = -1;
            for (int targetIndex = 0; targetIndex < targetParameters.Length; targetIndex++) {
                if (targetBindings[targetIndex] >= 0 || targetParameters[targetIndex].ParameterType != callerType)
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException($"Caller argument #{callerIndex + 1} ({callerType}) matches more than one target parameter by type. Use {nameof(CallerParameter<int>)}.{nameof(CallerParameter<int>.Named)}(...) to select one explicitly.");
                match = targetIndex;
            }
            if (match >= 0) targetBindings[match] = callerIndex;
        }

        return targetBindings;
    }

    private static int FindNamedTarget(ParameterInfo[] parameters, string name) {
        for (int i = 0; i < parameters.Length; i++)
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static void ValidateMappedBindings(ParameterAccessorGenerator.StackAccessor accessor, ParameterInfo[] targetParameters, MethodArgumentBinding[] bindings, Type sourceType, MethodInfo method) {
        for (int i = 0; i < bindings.Length; i++) {
            if (!bindings[i].IsMapped) continue;
            Type? valueType = accessor.GetValueType(bindings[i].Index);
            if (valueType is null)
                throw MissingMappedSource(sourceType, targetParameters[i], method);
            EnsureConvertible(valueType, targetParameters[i].ParameterType,
                $"mapped source for '{targetParameters[i].Name}'");
        }
    }

    private static Exception MissingMappedSource(Type sourceType, ParameterInfo parameter, MethodInfo method)
        => new InvalidOperationException($"Parameter '{parameter.Name}' on '{method}' cannot be resolved from {sourceType} and is not supplied by the caller.");

    private static void ValidateInstanceTarget(MethodInfo method, Type sourceType) {
        if (method.IsStatic) return;
        Type declaring = method.DeclaringType
            ?? throw new NotSupportedException("An instance method without a declaring type cannot be called.");
        if (!declaring.IsAssignableFrom(sourceType))
            throw new NotSupportedException($"Instance method '{method}' can only be generated when the mapped source ({sourceType}) is also the method instance ({declaring}). Bound external instances are not supported yet.");
    }

    private static void ValidateReturn(Type methodReturn, Type delegateReturn) {
        if (methodReturn == typeof(void) || delegateReturn == typeof(void)) {
            if (methodReturn != delegateReturn)
                throw new InvalidOperationException($"Target returns {methodReturn}, while the delegate returns {delegateReturn}.");
            return;
        }
        EnsureConvertible(methodReturn, delegateReturn, "return value");
    }

    private static void EnsureConvertible(Type source, Type target, string description) {
        if (CanConvert(source, target)) return;
        throw new InvalidOperationException($"The {description} has type {source}, which cannot be passed as {target} without a user conversion.");
    }

    private static bool CanConvert(Type source, Type target) {
        if (source == target) return true;
        if (!source.IsValueType && target.IsAssignableFrom(source)) return true;
        if (source.IsValueType && (target == typeof(object) || target == typeof(ValueType) || target.IsInterface && target.IsAssignableFrom(source)))
            return true;
        return Nullable.GetUnderlyingType(target) == source;
    }

    private static void EmitConversion(ILGenerator il, Type source, Type target, string description) {
        if (source == target || !source.IsValueType && target.IsAssignableFrom(source)) return;
        if (source.IsValueType && (target == typeof(object) || target == typeof(ValueType))) {
            il.Emit(OpCodes.Box, source);
            return;
        }
        if (source.IsValueType && target.IsInterface && target.IsAssignableFrom(source)) {
            il.Emit(OpCodes.Box, source);
            il.Emit(OpCodes.Castclass, target);
            return;
        }
        if (Nullable.GetUnderlyingType(target) == source) {
            ConstructorInfo ctor = target.GetConstructor([source])
                ?? throw new InvalidOperationException($"Could not find the nullable constructor for {target}.");
            il.Emit(OpCodes.Newobj, ctor);
            return;
        }
        throw new InvalidOperationException($"The {description} has type {source}, which cannot be passed as {target} without a user conversion.");
    }

    private readonly struct MethodArgumentBinding {
        private MethodArgumentBinding(bool isMapped, int index) {
            IsMapped = isMapped;
            Index = index;
        }
        internal bool IsMapped { get; }
        internal int Index { get; }
        internal static MethodArgumentBinding Mapped(int mapperIndex) => new(true, mapperIndex);
        internal static MethodArgumentBinding Caller(int callerIndex) => new(false, callerIndex);
    }
}
