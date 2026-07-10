using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// The base for an attribute that changes how a parameter object's member is read, its presence rule, its
/// value, or both. The seam behind attributes like <see cref="ForBoolCondAttribute"/> and
/// <see cref="NotNullOrWhitespaceAttribute"/>, subclass it to define a custom rule of your own.
/// </summary>
public abstract class AccessorEmiterHandler : Attribute {
    /// <summary>Installs the presence and value readers for the member (or the whole type) this attribute is on.</summary>
    public abstract void HandleEmit(char varChar, IAccessorEmiter?[] usagePlans, IAccessorEmiter?[] valuePlans, Type type, MemberInfo? member, Mapper mapper);
}
/// <summary>One reader for a single key, the presence test or the value fetch, emitted as IL.</summary>
public interface IAccessorEmiter {
    /// <summary>Emits the IL that reads this key.</summary>
    public abstract void Emit(ILGenerator il);
    /// <summary>
    /// Helper to load the instance and access the specific member.
    /// Handles the difference between ref structs (void*) and class references.
    /// </summary>
    public static void EmitMemberLoad(ILGenerator il, Type targetType, MemberInfo member) {
        if (member is FieldInfo field) {
            if (field.IsStatic)
                il.Emit(OpCodes.Ldsfld, field);
            else {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldfld, field);
            }
        }
        else {
            var meth = member as MethodInfo;
            if (meth is null) {
                if (member is PropertyInfo prop)
                    meth = prop.GetMethod!;
                if (meth is null)
                    throw new Exception("The member must be a field, property or method");
            }
            if (meth.IsStatic)
                il.Emit(OpCodes.Call, meth);
            else {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(targetType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, meth);
            }
        }
    }
}
/// <summary>Generate the IL emit to get the usage at a specific index (field / prop)</summary>
public class MemberUsageEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public void Emit(ILGenerator il) {

        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;

        if (!mType.IsValueType) {
            IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
        }
        else if (Nullable.GetUnderlyingType(mType) != null) {
            IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
            var local = il.DeclareLocal(mType);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Call, mType.GetProperty("HasValue")!.GetMethod!);
        }
        else
            il.Emit(OpCodes.Ldc_I4_1);
    }
}

/// <summary>Generate the IL emit to get the value at a specific index (field / prop)</summary>
public class MemberValueEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public void Emit(ILGenerator il) {
        IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
        if (mType.IsValueType)
            il.Emit(OpCodes.Box, mType);
    }
}