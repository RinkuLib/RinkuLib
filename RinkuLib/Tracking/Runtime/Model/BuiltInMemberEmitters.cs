using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Provides equality checks used by generated members.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class RuntimeTrackingComparison
{
    /// <summary>Returns whether two values differ.</summary>
    public static bool Different<T>(T left, T right) => !EqualityComparer<T>.Default.Equals(left, right);
}

/// <summary>Reads a member directly from the accepted original.</summary>
public sealed class RuntimeOriginalReadOnlyEmitter<TOriginal> : RuntimeTrackingMemberEmitter<TOriginal>
{
    private readonly RuntimeOriginalMemberAccess _access;

    internal RuntimeOriginalReadOnlyEmitter(RuntimeOriginalMemberAccess access) => _access = access;

    internal RuntimeOriginalMemberAccess Access => _access;

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override bool UsesSnapshot => false;

    /// <inheritdoc/>
    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context) { }
    /// <inheritdoc/>
    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il) => _access.EmitRead(context, il);
}

/// <summary>Edits an original member through snapshot storage.</summary>
public sealed class RuntimeOriginalSnapshotEmitter<TOriginal> : RuntimeTrackingMemberEmitter<TOriginal>
{
    private readonly RuntimeOriginalMemberAccess _access;

    internal RuntimeOriginalSnapshotEmitter(RuntimeOriginalMemberAccess access) => _access = access;

    internal RuntimeOriginalMemberAccess Access => _access;
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanWrite => _access.CanWrite;
    /// <inheritdoc/>
    public override bool UsesSnapshot => true;

    /// <inheritdoc/>
    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.SnapshotField = context.Type.SnapshotBuilder.DefineField(context.Type.NextSnapshotFieldName(context.Member.Name), context.Member.ValueType, FieldAttributes.Public);

    /// <inheritdoc/>
    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        Label original = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, original);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(original);
        il.Emit(OpCodes.Pop);
        _access.EmitRead(context, il);
    }

    /// <inheritdoc/>
    protected internal override void EmitSet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        if (!CanWrite) throw new InvalidOperationException($"Runtime member '{context.Member.Name}' is read-only.");
        context.EmitEnsureEdit(il);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitInitializeSnapshot(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitHasChange(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Call, DifferentMethod(context.Member.ValueType));
    }

    /// <inheritdoc/>
    protected internal override void EmitConfirm(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        if (!_access.CanWrite) return;
        Label done = il.DefineLabel();
        EmitHasChange(context, il);
        il.Emit(OpCodes.Brfalse, done);
        _access.EmitWrite(context, il, value =>
        {
            context.EmitLoadEdit(value);
            value.Emit(OpCodes.Ldfld, SnapshotField(context));
        });
        il.MarkLabel(done);
    }

    /// <inheritdoc/>
    protected internal override void EmitOriginalValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        BoxIfNeeded(context.Member.ValueType, il);
    }

    /// <inheritdoc/>
    protected internal override void EmitEditValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        BoxIfNeeded(context.Member.ValueType, il);
    }

    internal RuntimeOriginalReadOnlyEmitter<TOriginal> AsReadOnly() => new(_access);

    private static FieldBuilder SnapshotField(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.SnapshotField ?? throw new InvalidOperationException($"Runtime member '{context.Member.Name}' has no snapshot field.");

    internal static MethodInfo DifferentMethod(Type type)
        => typeof(RuntimeTrackingComparison).GetMethod(nameof(RuntimeTrackingComparison.Different), BindingFlags.Static | BindingFlags.Public)?.MakeGenericMethod(type)
           ?? throw new MissingMethodException(typeof(RuntimeTrackingComparison).FullName, nameof(RuntimeTrackingComparison.Different));

    internal static void BoxIfNeeded(Type type, ILGenerator il)
    {
        if (type.IsValueType) il.Emit(OpCodes.Box, type);
    }
}

/// <summary>Stores a member directly on the generated item.</summary>
public sealed class RuntimeDirectFieldEmitter<TOriginal> : RuntimeTrackingMemberEmitter<TOriginal>
{
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanWrite => true;
    /// <inheritdoc/>
    public override bool UsesSnapshot => false;

    /// <inheritdoc/>
    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.DirectField = context.Type.TypeBuilder.DefineField(context.Type.NextDirectFieldName(context.Member.Name), context.Member.ValueType, FieldAttributes.Private);

    /// <inheritdoc/>
    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DirectField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitSet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, DirectField(context));
    }

    private static FieldBuilder DirectField(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.DirectField ?? throw new InvalidOperationException($"Runtime member '{context.Member.Name}' has no direct field.");
}

/// <summary>Runtime-owned accepted state that still participates in the lazy edit snapshot.</summary>
public sealed class RuntimeDirectSnapshotEmitter<TOriginal> : RuntimeTrackingMemberEmitter<TOriginal>
{
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanWrite => true;
    /// <inheritdoc/>
    public override bool UsesSnapshot => true;

    /// <inheritdoc/>
    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context)
    {
        context.DirectField = context.Type.TypeBuilder.DefineField(context.Type.NextDirectFieldName(context.Member.Name), context.Member.ValueType, FieldAttributes.Private);
        context.SnapshotField = context.Type.SnapshotBuilder.DefineField(context.Type.NextSnapshotFieldName(context.Member.Name), context.Member.ValueType, FieldAttributes.Public);
    }

    /// <inheritdoc/>
    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        Label accepted = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, accepted);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accepted);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DirectField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitSet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        context.EmitEnsureEdit(il);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitInitializeSnapshot(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DirectField(context));
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitHasChange(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DirectField(context));
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Call, RuntimeOriginalSnapshotEmitter<TOriginal>.DifferentMethod(context.Member.ValueType));
    }

    /// <inheritdoc/>
    protected internal override void EmitConfirm(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        Label done = il.DefineLabel();
        EmitHasChange(context, il);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Stfld, DirectField(context));
        il.MarkLabel(done);
    }

    /// <inheritdoc/>
    protected internal override void EmitOriginalValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DirectField(context));
        RuntimeOriginalSnapshotEmitter<TOriginal>.BoxIfNeeded(context.Member.ValueType, il);
    }

    /// <inheritdoc/>
    protected internal override void EmitEditValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        RuntimeOriginalSnapshotEmitter<TOriginal>.BoxIfNeeded(context.Member.ValueType, il);
    }

    private static FieldBuilder DirectField(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.DirectField ?? throw new InvalidOperationException($"Runtime member '{context.Member.Name}' has no accepted field.");
    private static FieldBuilder SnapshotField(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.SnapshotField ?? throw new InvalidOperationException($"Runtime member '{context.Member.Name}' has no snapshot field.");
}

/// <summary>Edits a detached copy of a nested member.</summary>
public sealed class RuntimeNestedSnapshotEmitter<TOriginal> : RuntimeTrackingMemberEmitter<TOriginal>
{
    private readonly RuntimeOriginalMemberAccess _access;
    private readonly NestedEditMode _mode;
    private readonly MemberInfo[] _paths;

    internal RuntimeNestedSnapshotEmitter(RuntimeOriginalMemberAccess access, NestedEditMode mode)
    {
        _access = access;
        _mode = mode;
        if (mode == NestedEditMode.InPlace && (access.ValueType.IsValueType || access.ValueType == typeof(string)))
            throw new InvalidOperationException($"Nested in-place editing requires a reference member; {access.Member} is {access.ValueType}.");
        if (mode == NestedEditMode.Replacement && !access.CanWrite)
            throw new InvalidOperationException($"Nested replacement member {access.Member} has no writer.");
        _paths = FindPaths(access.ValueType);
    }

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanWrite => _access.CanWrite;
    /// <inheritdoc/>
    public override bool UsesSnapshot => true;

    /// <inheritdoc/>
    protected internal override void DefineStorage(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.SnapshotField = context.Type.SnapshotBuilder.DefineField(context.Type.NextSnapshotFieldName(context.Member.Name), context.Member.ValueType, FieldAttributes.Public);

    /// <inheritdoc/>
    protected internal override void EmitGet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        Label original = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, original);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(original);
        il.Emit(OpCodes.Pop);
        _access.EmitRead(context, il);
    }

    /// <inheritdoc/>
    protected internal override void EmitSet(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        if (!CanWrite) throw new InvalidOperationException($"Nested root '{context.Member.Name}' is read-only; mutate it through an ensured edit/path instead.");
        context.EmitEnsureEdit(il);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitInitializeSnapshot(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        il.Emit(OpCodes.Call, CopierMethod(context.Member.ValueType, nameof(RuntimeNestedCopier<object>.Clone)));
        il.Emit(OpCodes.Stfld, SnapshotField(context));
    }

    /// <inheritdoc/>
    protected internal override void EmitHasChange(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        il.Emit(OpCodes.Call, CopierMethod(context.Member.ValueType, nameof(RuntimeNestedCopier<object>.HasChanges)));
    }

    /// <inheritdoc/>
    protected internal override void EmitConfirm(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        Label done = il.DefineLabel();
        EmitHasChange(context, il);
        il.Emit(OpCodes.Brfalse, done);

        if (_mode == NestedEditMode.InPlace)
        {
            _access.EmitRead(context, il);
            context.EmitLoadEdit(il);
            il.Emit(OpCodes.Ldfld, SnapshotField(context));
            il.Emit(OpCodes.Call, CopierMethod(context.Member.ValueType, nameof(RuntimeNestedCopier<object>.CopyInPlace)));
        }
        else
        {
            _access.EmitWrite(context, il, value =>
            {
                context.EmitLoadEdit(value);
                value.Emit(OpCodes.Ldfld, SnapshotField(context));
            });
        }

        il.MarkLabel(done);
    }

    /// <inheritdoc/>
    protected internal override void EmitOriginalValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        _access.EmitRead(context, il);
        RuntimeOriginalSnapshotEmitter<TOriginal>.BoxIfNeeded(context.Member.ValueType, il);
    }

    /// <inheritdoc/>
    protected internal override void EmitEditValueAsObject(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldfld, SnapshotField(context));
        RuntimeOriginalSnapshotEmitter<TOriginal>.BoxIfNeeded(context.Member.ValueType, il);
    }

    /// <inheritdoc/>
    protected internal override IReadOnlyList<MemberInfo> GetNestedRuntimePathMembers() => _paths;

    internal RuntimeOriginalMemberAccess Access => _access;

    private static FieldBuilder SnapshotField(RuntimeTrackingMemberEmitContext<TOriginal> context)
        => context.SnapshotField ?? throw new InvalidOperationException($"Nested runtime member '{context.Member.Name}' has no snapshot field.");

    private static MethodInfo CopierMethod(Type valueType, string name)
        => typeof(RuntimeNestedCopier<>).MakeGenericType(valueType).GetMethod(name, BindingFlags.Static | BindingFlags.Public)
           ?? throw new MissingMethodException(typeof(RuntimeNestedCopier<>).FullName, name);

    private static MemberInfo[] FindPaths(Type type)
    {
        if (type == typeof(string)) return [];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var members = new List<MemberInfo>();
        foreach (PropertyInfo property in type.GetProperties(flags))
            if (property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0) members.Add(property);
        foreach (FieldInfo field in type.GetFields(flags))
            if (!field.IsStatic) members.Add(field);
        members.Sort(static (x, y) => string.CompareOrdinal(x.Name, y.Name));
        return members.ToArray();
    }
}
