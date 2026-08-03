using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.Tracking;
/// <summary>
/// Marks a field to be cloned by invoking an instance method on the container.
/// </summary>
/// <remarks>
/// The target method should return the value to be assigned to the field. 
/// </remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CopyUsingMethodAttribute(string methodName) : CopyFieldAttribute {
    private readonly string _methodName = methodName;
    /// <inheritdoc/>
    public override void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone) {
        Type declaringType = field.DeclaringType!;

        MethodInfo method = declaringType.GetMethod(_methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new RinkuTrackingException(ErrorCodes.CopyMethodNotUsable, $"Method '{_methodName}' not found on '{declaringType}'.");

        if (method.GetParameters().Length != 0)
            throw new RinkuTrackingException(ErrorCodes.CopyMethodNotUsable, $"Method '{_methodName}' on '{declaringType}' must have zero parameters.");

        if (!field.FieldType.IsAssignableFrom(method.ReturnType))
            throw new RinkuTrackingException(ErrorCodes.CopyMethodNotUsable, $"Method '{_methodName}' returns '{method.ReturnType}', which cannot be assigned to field '{field.Name}' ({field.FieldType}).");

        if (declaringType.IsValueType)
            il.Emit(OpCodes.Ldloca_S, clone);
        else
            il.Emit(OpCodes.Ldloc, clone);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);

        il.Emit(OpCodes.Stfld, field);
    }
}