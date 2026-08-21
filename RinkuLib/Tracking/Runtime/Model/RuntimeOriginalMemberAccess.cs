using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal sealed class RuntimeOriginalMemberAccess
{
    private RuntimeOriginalMemberAccess(MemberInfo member, Type valueType, bool canWrite)
    {
        Member = member;
        ValueType = valueType;
        CanWrite = canWrite;
    }

    internal MemberInfo Member { get; }
    internal Type ValueType { get; }
    internal bool CanWrite { get; }

    internal static RuntimeOriginalMemberAccess Create(MemberInfo member)
        => member switch
        {
            PropertyInfo property when property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0
                => new(property, property.PropertyType, property.SetMethod?.IsPublic == true),
            FieldInfo field when !field.IsStatic
                => new(field, field.FieldType, !field.IsInitOnly && !field.IsLiteral),
            _ => throw new NotSupportedException($"{member} is not a supported original member.")
        };

    internal void EmitRead<TOriginal>(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il)
    {
        context.Type.EmitLoadOriginalTarget(il);
        EmitReadFromLoadedTarget(il, typeof(TOriginal));
    }

    internal void EmitWrite<TOriginal>(RuntimeTrackingMemberEmitContext<TOriginal> context, ILGenerator il, Action<ILGenerator> emitValue)
    {
        if (!CanWrite) throw new InvalidOperationException($"Original member {Member} is read-only.");

        context.Type.EmitLoadOriginalForWrite(il);
        emitValue(il);
        if (Member is PropertyInfo property)
        {
            MethodInfo setter = property.SetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"set_{property.Name}");
            il.Emit(typeof(TOriginal).IsValueType || !setter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, setter);
        }
        else
        {
            il.Emit(OpCodes.Stfld, (FieldInfo)Member);
        }
    }

    internal void EmitReadFromLoadedTarget(ILGenerator il, Type targetType)
    {
        if (Member is PropertyInfo property)
        {
            MethodInfo getter = property.GetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"get_{property.Name}");
            il.Emit(targetType.IsValueType || !getter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, getter);
        }
        else
        {
            il.Emit(OpCodes.Ldfld, (FieldInfo)Member);
        }
    }
}
