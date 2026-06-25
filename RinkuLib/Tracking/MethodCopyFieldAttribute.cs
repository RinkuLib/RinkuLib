using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.Tracking;
/// <summary>
/// Base class for attributes that perform field-level cloning via method injection.
/// </summary>
public abstract class MethodCopyFieldAttribute : CopyFieldAttribute {
    /// <summary>
    /// Resolves the method to be called during the cloning process for the given field type.
    /// </summary>
    protected abstract MethodInfo GetMethod(FieldInfo field);
    /// <inheritdoc/>
    public sealed override void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone) {
        if (field.DeclaringType!.IsValueType)
            il.Emit(OpCodes.Ldloca_S, clone);
        else
            il.Emit(OpCodes.Ldloc, clone);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);

        il.Emit(OpCodes.Call, GetMethod(field));
        il.Emit(OpCodes.Stfld, field);
    }
}
/// <summary>Marks a field to be cloned using <see cref="CopyExtensions.Copy{T}"/>.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CopyAttribute : MethodCopyFieldAttribute {
    /// <inheritdoc/>
    protected override MethodInfo GetMethod(FieldInfo field) 
        => typeof(CopyExtensions)
            .GetMethod(nameof(CopyExtensions.Copy))!
            .MakeGenericMethod(field.FieldType);
}
/// <summary>Marks a collection field to be shallow-copied.</summary>
/// <remarks>Clones the collection container but shares element references.</remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ShallowCollectionAttribute : MethodCopyFieldAttribute {
    /// <inheritdoc/>
    protected override MethodInfo GetMethod(FieldInfo field)
        => typeof(CollectionCopyExtensions)
            .GetMethod(nameof(CollectionCopyExtensions.ShallowCopy))!
            .MakeGenericMethod(field.FieldType);
}
/// <summary>Marks a collection field to be deep-copied.</summary>
/// <remarks>Clones the collection container and recursively triggers element cloning.</remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DeepCollectionAttribute : MethodCopyFieldAttribute {
    /// <inheritdoc/>
    protected override MethodInfo GetMethod(FieldInfo field)
        => typeof(CollectionCopyExtensions)
            .GetMethod(nameof(CollectionCopyExtensions.DeepCopy))!
            .MakeGenericMethod(field.FieldType);
}