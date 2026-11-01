using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Provides data and helpers while a tracking type is emitted.</summary>
public sealed class RuntimeTrackingEmitContext<TOriginal>
{
    private Action<ILGenerator, string?>? _changedEmitter;
    internal FieldBuilder ConfirmationField { get; set; } = null!;
    private int _snapshotFieldIndex;
    private int _directFieldIndex;
    private readonly List<Action<Type>> _initializers = [];
    private readonly List<Action> _deferredEmitters = [];

    internal void DeferEmission(Action emit) => _deferredEmitters.Add(emit);
    internal void CompleteEmission() {
        for (int i = 0; i < _deferredEmitters.Count; i++)
            _deferredEmitters[i]();
    }

    internal RuntimeTrackingEmitContext(RuntimeTrackingTypeDefinition<TOriginal> definition, TypeBuilder typeBuilder, TypeBuilder snapshotBuilder, FieldBuilder originalField, FieldBuilder editField, FieldBuilder isNewField, FieldBuilder mapperField)
    {
        Definition = definition;
        TypeBuilder = typeBuilder;
        SnapshotBuilder = snapshotBuilder;
        OriginalField = originalField;
        HasOriginalField = typeBuilder.DefineField("_hasOriginal", typeof(bool), FieldAttributes.Private);
        EditField = editField;
        IsNewField = isNewField;
        MapperField = mapperField;
    }

    /// <summary>Gets the generation definition.</summary>
    public RuntimeTrackingTypeDefinition<TOriginal> Definition { get; }
    /// <summary>Gets the generated item builder.</summary>
    public TypeBuilder TypeBuilder { get; }
    /// <summary>Gets the snapshot type builder.</summary>
    public TypeBuilder SnapshotBuilder { get; }
    /// <summary>Gets the original value field.</summary>
    public FieldBuilder OriginalField { get; }
    /// <summary>Gets the accepted-original availability field.</summary>
    public FieldBuilder HasOriginalField { get; }
    /// <summary>Gets the edit snapshot field.</summary>
    public FieldBuilder EditField { get; }
    /// <summary>Gets the new state field.</summary>
    public FieldBuilder IsNewField { get; }
    /// <summary>Gets the runtime member map field.</summary>
    public FieldBuilder MapperField { get; }
    /// <summary>Gets the method that ensures an edit snapshot.</summary>
    public MethodBuilder? EnsureEditMethod { get; internal set; }

    internal Dictionary<RuntimeTrackingMemberDefinition<TOriginal>, RuntimeTrackingMemberEmitContext<TOriginal>> Members { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<RuntimeTrackingMemberDefinition<TOriginal>, RuntimeEmittedProperty> Properties { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Adds setup to run after the CLR type is created.</summary>
    public void AddInitializer(Action<Type> initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        _initializers.Add(initializer);
    }

    internal void Initialize(Type generatedType)
    {
        for (int i = 0; i < _initializers.Count; i++) _initializers[i](generatedType);
    }

    /// <summary>Sets the change notification emitter.</summary>
    public void SetChangedEmitter(Action<ILGenerator, string?> emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        _changedEmitter = emitter;
    }

    internal string NextSnapshotFieldName(string member) => $"_e{_snapshotFieldIndex++}_{Sanitize(member)}";
    internal string NextDirectFieldName(string member) => $"_v{_directFieldIndex++}_{Sanitize(member)}";

    /// <summary>Emits a change notification when one is configured.</summary>
    public void EmitChanged(ILGenerator il, string? propertyName) {
        _changedEmitter?.Invoke(il, propertyName);
        if (propertyName is null || _changedEmitter is null)
            return;
        var notified = new HashSet<string>(StringComparer.Ordinal) { propertyName };
        NotifyDependents(propertyName);
        void NotifyDependents(string dependency) {
            foreach (var member in Definition.Members)
                if (member.Emitter is IRuntimeComputedMember computed)
                    foreach (string source in computed.Dependencies)
                        if ((source == dependency || dependency.StartsWith(source + ".", StringComparison.Ordinal)) && notified.Add(member.Name)) {
                            _changedEmitter(il, member.Name);
                            NotifyDependents(member.Name);
                        }
        }
    }

    /// <summary>Emits a load of the original value.</summary>
    public void EmitLoadOriginalValue(ILGenerator il)
        => Definition.OriginalStorage.EmitLoadValue(il, OriginalField);

    /// <summary>Emits a load of the original target.</summary>
    public void EmitLoadOriginalTarget(ILGenerator il)
        => Definition.OriginalStorage.EmitLoadTarget(il, OriginalField);

    /// <summary>Emits a load of the edit snapshot.</summary>
    public void EmitLoadEdit(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, EditField);
    }

    /// <summary>Emits a call that ensures an edit snapshot.</summary>
    public void EmitEnsureEdit(ILGenerator il)
    {
        MethodBuilder method = EnsureEditMethod ?? throw new InvalidOperationException("EnsureEdit has not been emitted yet.");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, method);
    }

    internal RuntimeTrackingMemberEmitContext<TOriginal> MemberContext(RuntimeTrackingMemberDefinition<TOriginal> member)
        => Members.TryGetValue(member, out RuntimeTrackingMemberEmitContext<TOriginal>? context)
            ? context
            : throw new InvalidOperationException($"Runtime member '{member.Name}' has no emission context.");

    private static string Sanitize(string value)
    {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars);
    }
}

internal readonly record struct RuntimeEmittedProperty(PropertyBuilder Property, MethodBuilder Getter, MethodBuilder? Setter);
