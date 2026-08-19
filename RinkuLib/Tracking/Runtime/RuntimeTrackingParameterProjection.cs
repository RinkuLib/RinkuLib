using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Querying;
using Rinku.Querying.Parameters;

namespace Rinku.Tracking.Runtime;

// Attached to every generated tracking CLR type. Core already discovers AccessorEmitterHandler
// attributes once while it builds/caches a parameter accessor, so this adds no per-call reflection.
/// <summary>Connects generated tracking members to parameter projection.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true)]
public sealed class RuntimeTrackingParameterSourceAttribute : AccessorEmitterHandler {
    /// <summary>Resolves a generated member parameter emitter.</summary>
    public override IAccessorEmitter? GetMemberEmitter(char varChar, int index, Type type, MemberInfo member, Mapper mapper) {
        if (!RuntimeTrackingParameterRegistry.TryGet(type, out RuntimeTrackingParameterShape? shape)) return null;
        if (!shape!.TryGetProperty(member.Name, out RuntimeTrackingParameterMember? trackingMember)) return null;

        ReadOnlySpan<char> key = GetName(varChar, mapper.Keys[index]);
        if (!shape.TryGetParameter(key, out RuntimeTrackingParameterMember? mapped))
            return RuntimeTrackingDisabledParameterEmitter.Instance;
        if (!ReferenceEquals(mapped, trackingMember))
            return CreateMemberAdapter(mapped!, varChar, index, type, mapper);

        // Interface-typed generic calls do not expose the generated CLR type, so enforce the
        // generated parameter shape directly against the declared contract member.
        if (type.IsInterface) return ResolveEmitter(trackingMember!, varChar, index, type, mapper);

        // Generated runtime members are runtime-presence sources too. Route them through the adapter so
        // the UseWith emitter can mark only members that are actually available on this instance while
        // still preserving member-level AccessorEmitterHandler precedence.
        return CreateMemberAdapter(trackingMember!, varChar, index, type, mapper);
    }

    /// <summary>Resolves a generated type parameter emitter.</summary>
    public override ITypeAccessorEmitter? GetTypeEmitter(char varChar, int index, Type type, Mapper mapper) {
        if (!RuntimeTrackingParameterRegistry.TryGet(type, out RuntimeTrackingParameterShape? shape)) return null;
        ReadOnlySpan<char> key = GetName(varChar, mapper.Keys[index]);
        if (!shape!.TryGetParameter(key, out RuntimeTrackingParameterMember? member)) return null;

        return new RuntimeTrackingTypeEmitterAdapter(member!.Property, ResolveEmitter(member, varChar, index, type, mapper));
    }

    private static IAccessorEmitter CreateMemberAdapter(RuntimeTrackingParameterMember member, char varChar, int index, Type type, Mapper mapper)
        => new RuntimeTrackingMemberEmitterAdapter(member.Property, ResolveEmitter(member, varChar, index, type, mapper));

    private static IAccessorEmitter ResolveEmitter(RuntimeTrackingParameterMember member, char varChar, int index, Type type, Mapper mapper) {
        // Runtime-only properties can still carry normal Rinku parameter metadata copied from the original/contract.
        AccessorEmitterHandler? custom = member.Property.GetCustomAttribute<AccessorEmitterHandler>();
        return custom?.GetMemberEmitter(varChar, index, type, member.Property, mapper)
            ?? RuntimeTrackingDefaultParameterEmitter.Instance;
    }

    private static ReadOnlySpan<char> GetName(char varChar, string key) {
        ReadOnlySpan<char> span = key;
        return span.Length != 0 && varChar != default && span[0] == varChar ? span[1..] : span;
    }
}

internal static class RuntimeTrackingParameterRegistry {
    private static readonly ConcurrentDictionary<Type, RuntimeTrackingParameterShape> Shapes = new();

    internal static void Register(Type generatedType, IReadOnlyList<IRuntimeTrackingMember> members) {
        var shape = new RuntimeTrackingParameterShape(generatedType, members);
        Shapes.TryAdd(generatedType, shape);
    }

    internal static bool TryGet(Type type, out RuntimeTrackingParameterShape? shape) => Shapes.TryGetValue(type, out shape);
}

internal sealed class RuntimeTrackingParameterShape {
    private readonly Dictionary<string, RuntimeTrackingParameterMember> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeTrackingParameterMember> _properties = new(StringComparer.OrdinalIgnoreCase);

    internal RuntimeTrackingParameterShape(Type generatedType, IReadOnlyList<IRuntimeTrackingMember> members) {
        ParameterConflictBehavior conflictBehavior = generatedType.GetCustomAttribute<ParameterConflictAttribute>(inherit: true)?.Behavior
            ?? ParameterConflictBehavior.Throw;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (int i = 0; i < members.Count; i++) {
            IRuntimeTrackingMember member = members[i];
            PropertyInfo? property = generatedType.GetProperty(member.Name, flags);
            if (property is null) continue;
            var info = new RuntimeTrackingParameterMember(member, property);
            _properties.Add(member.Name, info);
            if (!member.IncludeInParameters || property.IsDefined(typeof(ParameterIgnoreAttribute), inherit: true)) continue;

            ParameterNameAttribute? rename = property.GetCustomAttribute<ParameterNameAttribute>(inherit: true);
            IReadOnlyList<string>? legacyNames = member.ParameterNames;
            if (rename is not null) Add(rename.Name, info, conflictBehavior);
            else if (legacyNames is { Count: > 0 })
                for (int n = 0; n < legacyNames.Count; n++) Add(legacyNames[n], info, conflictBehavior);
            else
                Add(member.Name, info, conflictBehavior);

            foreach (ParameterAliasAttribute alias in property.GetCustomAttributes<ParameterAliasAttribute>(inherit: true))
                Add(alias.Name, info, conflictBehavior);
        }
    }

    internal bool TryGetParameter(ReadOnlySpan<char> name, out RuntimeTrackingParameterMember? member) {
        // Mapper names are strings already and accessor creation is one-time; this allocation is on the
        // cold plan-building path only. A future Core resolver API can pass mapper indexes directly.
        return _parameters.TryGetValue(name.ToString(), out member);
    }

    internal bool TryGetProperty(string name, out RuntimeTrackingParameterMember? member) => _properties.TryGetValue(name, out member);

    private void Add(string name, RuntimeTrackingParameterMember member, ParameterConflictBehavior conflictBehavior) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = NormalizeName(name);
        if (normalized.Length == 0) throw new ArgumentException("A runtime parameter name cannot contain only a variable prefix.", nameof(name));
        if (_parameters.TryGetValue(normalized, out RuntimeTrackingParameterMember? existing) && !ReferenceEquals(existing, member)) {
            if (conflictBehavior == ParameterConflictBehavior.TakeOne) return;
            throw new InvalidOperationException($"Runtime parameter name '{name}' is provided by both '{existing.Member.Name}' and '{member.Member.Name}'.");
        }
        _parameters[normalized] = member;
    }

    private static string NormalizeName(string name) {
        char first = name[0];
        return !char.IsLetterOrDigit(first) && first != '_' ? name[1..] : name;
    }
}

internal sealed class RuntimeTrackingParameterMember {
    internal RuntimeTrackingParameterMember(IRuntimeTrackingMember member, PropertyInfo property) {
        Member = member;
        Property = property;
    }

    internal IRuntimeTrackingMember Member { get; }
    internal PropertyInfo Property { get; }
}

internal sealed class RuntimeTrackingMemberEmitterAdapter(PropertyInfo source, IAccessorEmitter emitter) : IAccessorEmitter {
    public void Validate(Type type, MemberInfo member) => emitter.Validate(type, source);
    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => emitter.Emit(il, index, key, type, source, handlerValues, handlerIndex, handlerValue, bindValue);
    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context)
        => emitter.EmitUseWith(il, index, type, source, bindValue, context);
    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member)
        => emitter.EmitStackUsage(il, type, source);
    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member)
        => emitter.EmitStackValue(il, type, source);
    public Type GetStackType(Type type, MemberInfo member)
        => emitter.GetStackType(type, source);
}

internal sealed class RuntimeTrackingTypeEmitterAdapter(PropertyInfo member, IAccessorEmitter emitter) : ITypeAccessorEmitter {
    public void Validate(Type type) => emitter.Validate(type, member);
    public void Emit(ILGenerator il, int index, string key, Type type, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => emitter.Emit(il, index, key, type, member, handlerValues, handlerIndex, handlerValue, bindValue);
    public void EmitUseWith(ILGenerator il, int index, Type type, bool bindValue, UseWithEmissionContext context)
        => emitter.EmitUseWith(il, index, type, member, bindValue, context);
    public void EmitStackUsage(ILGenerator il, Type type)
        => emitter.EmitStackUsage(il, type, member);
    public void EmitStackValue(ILGenerator il, Type type)
        => emitter.EmitStackValue(il, type, member);
    public Type GetStackType(Type type)
        => emitter.GetStackType(type, member);
}

internal sealed class RuntimeTrackingDefaultParameterEmitter : IAccessorEmitter {
    internal static readonly RuntimeTrackingDefaultParameterEmitter Instance = new();

    public void Validate(Type type, MemberInfo member) { }

    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member, LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue)
        => AccessorEmitter.EmitSlot(il, index, key, handlerValues, handlerIndex, handlerValue, bindValue,
            e => EmitUsage(e, type, member), e => AccessorEmitter.EmitMemberValue(e, type, member));

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue, UseWithEmissionContext context) {
        var skip = il.DefineLabel();
        EmitUsage(il, type, member, context.SourceArgument);
        il.Emit(OpCodes.Brfalse, skip);
        AccessorEmitter.MarkUseWithSlot(il, index, context);
        AccessorEmitter.EmitUseWithValue(il, index, bindValue,
            e => AccessorEmitter.EmitMemberValue(e, type, member, context.SourceArgument), context);
        il.MarkLabel(skip);
    }

    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member)
        => EmitUsage(il, type, member);

    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberLoad(il, type, member);

    public Type GetStackType(Type type, MemberInfo member)
        => ParameterMemberAccess.GetMemberType(member);

    private static void EmitUsage(ILGenerator il, Type type, MemberInfo member, int sourceArgument = 0) {
        Type memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (!memberType.IsValueType) {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }
        if (Nullable.GetUnderlyingType(memberType) is not null) {
            AccessorEmitter.EmitMemberLoad(il, type, member, sourceArgument);
            LocalBuilder value = il.DeclareLocal(memberType);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, memberType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            return;
        }
        il.Emit(OpCodes.Ldc_I4_1);
    }
}

internal sealed class RuntimeTrackingDisabledParameterEmitter : IAccessorEmitter {
    internal static readonly RuntimeTrackingDisabledParameterEmitter Instance = new();

    public void Validate(Type type, MemberInfo member) { }

    public void Emit(ILGenerator il, int index, string key, Type type, MemberInfo member,
        LocalBuilder? handlerValues, int handlerIndex, bool handlerValue, bool bindValue) {
        // Direct access starts with a cleared usage span, so intentionally doing nothing disables the slot.
    }

    public void EmitUseWith(ILGenerator il, int index, Type type, MemberInfo member, bool bindValue,
        UseWithEmissionContext context) {
        // UseWith reuses the builder value array, therefore excluded members must actively clear their slot.
        AccessorEmitter.ClearUseWithSlot(il, index, context);
    }

    public void EmitStackUsage(ILGenerator il, Type type, MemberInfo member)
        => il.Emit(OpCodes.Ldc_I4_0);

    public void EmitStackValue(ILGenerator il, Type type, MemberInfo member)
        => AccessorEmitter.EmitMemberLoad(il, type, member);

    public Type GetStackType(Type type, MemberInfo member)
        => ParameterMemberAccess.GetMemberType(member);
}
