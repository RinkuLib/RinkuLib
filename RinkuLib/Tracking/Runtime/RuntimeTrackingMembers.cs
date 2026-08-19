using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal abstract class RuntimeTrackingMemberBase(string name, Type valueType, bool canWrite, bool includeInRuntimeAccess, bool includeInParameters, IReadOnlyList<string>? parameterNames, bool exposeProperty, IReadOnlyList<MemberInfo>? metadataSources) : IRuntimeTrackingMember {
    public string Name { get; } = name;
    public Type ValueType { get; } = valueType;
    public bool CanWrite { get; } = canWrite;
    public bool IncludeInRuntimeAccess { get; } = includeInRuntimeAccess;
    public bool IncludeInParameters { get; } = includeInParameters;
    public IReadOnlyList<string>? ParameterNames { get; } = parameterNames;
    public bool ExposeProperty { get; } = exposeProperty;

    public abstract void EmitGet(RuntimeTrackingMemberEmitContext context, ILGenerator il);
    public abstract void EmitSet(RuntimeTrackingMemberEmitContext context, ILGenerator il);

    public virtual void ApplyMetadata(PropertyBuilder property) {
        if (metadataSources is null || metadataSources.Count == 0) return;
        var attributes = new List<CustomAttributeData>();
        foreach (MemberInfo source in metadataSources)
            foreach (CustomAttributeData attribute in source.CustomAttributes) {
                if (typeof(IRuntimeTrackingMemberAttribute).IsAssignableFrom(attribute.AttributeType)) continue;
                AttributeUsageAttribute? usage = attribute.AttributeType.GetCustomAttribute<AttributeUsageAttribute>();
                if (usage is not null && (usage.ValidOn & AttributeTargets.Property) == 0) continue;
                if (usage?.AllowMultiple != true)
                    for (int i = attributes.Count - 1; i >= 0; i--)
                        if (attributes[i].AttributeType == attribute.AttributeType) attributes.RemoveAt(i);
                attributes.Add(attribute);
            }

        foreach (CustomAttributeData attribute in attributes)
            if (CustomAttributeCopy.TryCreate(attribute, out CustomAttributeBuilder? builder)) property.SetCustomAttribute(builder!);
    }
}

internal sealed class OriginalReadableRuntimeTrackingMember(string name, Type valueType, IRuntimeOriginalReader reader, bool includeInRuntimeAccess, bool includeInParameters, IReadOnlyList<string>? parameterNames, bool exposeProperty, IReadOnlyList<MemberInfo>? metadataSources)
    : RuntimeTrackingMemberBase(name, valueType, false, includeInRuntimeAccess, includeInParameters, parameterNames, exposeProperty, metadataSources) {
    public override void EmitGet(RuntimeTrackingMemberEmitContext context, ILGenerator il)
        => reader.EmitRead(il, context.EmitLoadOriginal);
    public override void EmitSet(RuntimeTrackingMemberEmitContext context, ILGenerator il)
        => throw new InvalidOperationException($"Runtime member '{Name}' is read-only.");
}

internal sealed class OriginalEditableRuntimeTrackingMember(string name, Type valueType, IRuntimeOriginalReader reader, IRuntimeOriginalWriter writer, bool includeInRuntimeAccess, bool includeInParameters, IReadOnlyList<string>? parameterNames, bool exposeProperty, IReadOnlyList<MemberInfo>? metadataSources)
    : RuntimeTrackingMemberBase(name, valueType, true, includeInRuntimeAccess, includeInParameters, parameterNames, exposeProperty, metadataSources), IRuntimeEditableTrackingMember {
    public override void EmitGet(RuntimeTrackingMemberEmitContext context, ILGenerator il)
        => context.EmitTrackedGet(il, ValueType, e => reader.EmitRead(e, context.EmitLoadOriginal));
    public override void EmitSet(RuntimeTrackingMemberEmitContext context, ILGenerator il)
        => context.EmitTrackedSetFromArgument(il, ValueType);
    public void EmitReadBaseline(ILGenerator il, Action<ILGenerator> emitOriginal) => reader.EmitRead(il, emitOriginal);
    public void EmitApply(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue)
        => writer.EmitWrite(il, emitOriginal, emitValue);
}

internal sealed class RuntimeStoredTrackingMember(string name, Type valueType, bool writable = true, bool includeInRuntimeAccess = true, bool includeInParameters = false, IReadOnlyList<string>? parameterNames = null, bool exposeProperty = true, IReadOnlyList<MemberInfo>? metadataSources = null)
    : RuntimeTrackingMemberBase(name, valueType, writable, includeInRuntimeAccess, includeInParameters, parameterNames, exposeProperty, metadataSources) {
    private string FieldKey => $"runtime-member:{Name}";

    public override void EmitGet(RuntimeTrackingMemberEmitContext context, ILGenerator il) {
        FieldBuilder field = context.GetOrAddInstanceField(FieldKey, ValueType, $"runtime_{Sanitize(Name)}");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
    }

    public override void EmitSet(RuntimeTrackingMemberEmitContext context, ILGenerator il) {
        if (!CanWrite) throw new InvalidOperationException($"Runtime member '{Name}' is read-only.");
        FieldBuilder field = context.GetOrAddInstanceField(FieldKey, ValueType, $"runtime_{Sanitize(Name)}");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        context.EmitRaiseChanged(il);
    }

    private static string Sanitize(string value) {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new(chars);
    }
}

/// <summary>Creates common generated tracking members.</summary>
public static class RuntimeTrackingMembers {
    /// <summary>Creates a stored runtime member.</summary>
    public static IRuntimeTrackingMember Stored<T>(string name, bool writable = true, bool includeInRuntimeAccess = true, bool exposeProperty = true, bool includeInParameters = false) => new RuntimeStoredTrackingMember(name, typeof(T), writable, includeInRuntimeAccess, includeInParameters, null, exposeProperty);

    /// <summary>Creates a runtime member from a property.</summary>
    public static IRuntimeTrackingMember From(PropertyInfo property, string? name = null, bool? editable = null, bool includeInRuntimeAccess = true, bool exposeProperty = true, bool includeInParameters = true) {
        ArgumentNullException.ThrowIfNull(property);
        var builder = new RuntimeTrackingMemberBuilder(property) {
            Name = name ?? property.Name,
            IsEditable = editable ?? property.SetMethod?.IsPublic == true,
            IncludeInRuntimeAccess = includeInRuntimeAccess,
            IncludeInParameters = includeInParameters,
            ExposeProperty = exposeProperty
        };
        return builder.Build();
    }

    /// <summary>Creates a runtime member from a field.</summary>
    public static IRuntimeTrackingMember From(FieldInfo field, string? name = null, bool? editable = null, bool includeInRuntimeAccess = true, bool exposeProperty = true, bool includeInParameters = true) {
        ArgumentNullException.ThrowIfNull(field);
        var builder = new RuntimeTrackingMemberBuilder(field) {
            Name = name ?? field.Name,
            IsEditable = editable ?? (!field.IsInitOnly && !field.IsLiteral),
            IncludeInRuntimeAccess = includeInRuntimeAccess,
            IncludeInParameters = includeInParameters,
            ExposeProperty = exposeProperty
        };
        return builder.Build();
    }

    /// <summary>Creates a runtime member from getter and setter methods.</summary>
    public static IRuntimeTrackingMember FromMethods<TOriginal, TValue>(string name, MethodInfo getter, MethodInfo? setter = null, bool includeInRuntimeAccess = true, bool exposeProperty = true, bool includeInParameters = true) {
        var builder = new RuntimeTrackingMemberBuilder(typeof(TOriginal), name, typeof(TValue));
        builder.ReadFrom(getter);
        if (setter is not null) builder.WriteWith(setter);
        builder.IsEditable = setter is not null;
        builder.IncludeInRuntimeAccess = includeInRuntimeAccess;
        builder.IncludeInParameters = includeInParameters;
        builder.ExposeProperty = exposeProperty;
        return builder.Build();
    }
}
