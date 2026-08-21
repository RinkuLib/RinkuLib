using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Expandable second-phase type contribution attached by first-phase options.</summary>
public interface IRuntimeTrackingTypeEmitter<TOriginal>
{
    /// <summary>Emits members for a generated type.</summary>
    void Emit(RuntimeTrackingEmitContext<TOriginal> context);
    /// <summary>Completes setup after the type is created.</summary>
    void Complete(RuntimeTrackingGeneratedType<TOriginal> type) { }
}

/// <summary>
/// Per-member emitter selected during configuration. The final emitter determines whether the member is snapshot-backed,
/// direct TEdit state, read-only original state, nested draft state, or something custom.
/// </summary>
public abstract class RuntimeTrackingMemberEmitter<TOriginal>
{
    /// <summary>Gets whether the member can be read.</summary>
    public abstract bool CanRead { get; }
    /// <summary>Gets whether the member can be written.</summary>
    public abstract bool CanWrite { get; }
    /// <summary>Gets whether the member uses snapshot storage.</summary>
    public abstract bool UsesSnapshot { get; }

    /// <summary>Defines storage for the member.</summary>
    protected internal abstract void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context);
    /// <summary>Emits a member read.</summary>
    protected internal abstract void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il);
    /// <summary>Emits a member write.</summary>
    protected internal virtual void EmitSet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
        => throw new InvalidOperationException($"Runtime member '{context.Member.Name}' is read-only.");

    /// <summary>Emits snapshot initialization.</summary>
    protected internal virtual void EmitInitializeSnapshot(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) { }
    /// <summary>Emits a change check.</summary>
    protected internal virtual void EmitHasChange(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
        => il.Emit(OpCodes.Ldc_I4_0);
    /// <summary>Emits member confirmation.</summary>
    protected internal virtual void EmitConfirm(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) { }
    /// <summary>Emits the accepted value as an object.</summary>
    protected internal virtual void EmitOriginalValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
        => EmitBoxedDefault(context.Member.ValueType, il);
    /// <summary>Emits the edited value as an object.</summary>
    protected internal virtual void EmitEditValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
        => EmitBoxedDefault(context.Member.ValueType, il);
    /// <summary>Gets nested members exposed through runtime paths.</summary>
    protected internal virtual IReadOnlyList<MemberInfo> GetNestedRuntimePathMembers() => Array.Empty<MemberInfo>();

    private static void EmitBoxedDefault(Type type, ILGenerator il)
    {
        if (!type.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
            return;
        }

        LocalBuilder value = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Box, type);
    }
}

/// <summary>Provides emission data for one generated member.</summary>
public sealed class RuntimeTrackingMemberEmitContext<TOriginal>
{
    internal RuntimeTrackingMemberEmitContext(RuntimeTrackingEmitContext<TOriginal> type, RuntimeTrackingMemberDefinition<TOriginal> member)
    {
        Type = type;
        Member = member;
    }

    /// <summary>Gets the type emission context.</summary>
    public RuntimeTrackingEmitContext<TOriginal> Type { get; }
    /// <summary>Gets the member definition.</summary>
    public RuntimeTrackingMemberDefinition<TOriginal> Member { get; }
    /// <summary>Gets the snapshot field when defined.</summary>
    public FieldBuilder? SnapshotField { get; internal set; }
    /// <summary>Gets the direct field when defined.</summary>
    public FieldBuilder? DirectField { get; internal set; }

    /// <summary>Emits a load of the accepted original.</summary>
    public void EmitLoadOriginal(ILGenerator il) => Type.EmitLoadOriginalValue(il);
    /// <summary>Emits a call that ensures an edit snapshot.</summary>
    public void EmitEnsureEdit(ILGenerator il) => Type.EmitEnsureEdit(il);
    /// <summary>Emits a load of the edit snapshot.</summary>
    public void EmitLoadEdit(ILGenerator il) => Type.EmitLoadEdit(il);
    /// <summary>Emits a change notification.</summary>
    public void EmitChanged(ILGenerator il, string? propertyName) => Type.EmitChanged(il, propertyName);
}

/// <summary>Describes a completed generated tracking type.</summary>
public sealed class RuntimeTrackingGeneratedType<TOriginal>
{
    internal RuntimeTrackingGeneratedType(Type type, RuntimeTrackingTypeDefinition<TOriginal> definition)
    {
        Type = type;
        Definition = definition;
    }

    /// <summary>Gets the generated CLR type.</summary>
    public Type Type { get; }
    /// <summary>Gets the definition used for generation.</summary>
    public RuntimeTrackingTypeDefinition<TOriginal> Definition { get; }
}
