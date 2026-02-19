using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.Queries;
/// <summary>Generate the IL emit to get the usage at a specific index</summary>
public abstract class AccessorEmitter {
    /// <summary>Generate the IL emit to get the usage at a specific index</summary>
    public abstract void Emit(ILGenerator il);
    /// <summary>
    /// Helper to load the instance and access the specific member.
    /// Handles the difference between ref structs (void*) and class references.
    /// </summary>
    public static void EmitMemberLoad(ILGenerator il, Type targetType, MemberInfo member) {
        il.Emit(OpCodes.Ldarg_1);
        if (member is FieldInfo f)
            il.Emit(OpCodes.Ldfld, f);
        else {
            var getter = ((PropertyInfo)member).GetMethod!;
            il.Emit(targetType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, getter);
        }
    }
}
/// <summary>Generate the IL emit to get the usage at a specific index (field / prop)</summary>
public class MemberUsageEmitter(Type targetType, MemberInfo member) : AccessorEmitter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public override void Emit(ILGenerator il) {

        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;

        if (!mType.IsValueType) {
            EmitMemberLoad(il, TargetType, _member);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Cgt_Un);
        }
        else if (Nullable.GetUnderlyingType(mType) != null) {
            EmitMemberLoad(il, TargetType, _member);
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
public class MemberValueEmitter(Type targetType, MemberInfo member) : AccessorEmitter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public override void Emit(ILGenerator il) {
        EmitMemberLoad(il, TargetType, _member);
        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
        if (mType.IsValueType)
            il.Emit(OpCodes.Box, mType);
    }
}
/// <summary>Generate the IL emit to get the usage of a condition at a specific index (field / prop)</summary>
public class MemberCondUsageEmitter(Type targetType, MemberInfo member) : AccessorEmitter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    /// <inheritdoc/>
    public override void Emit(ILGenerator il) 
        => EmitMemberLoad(il, TargetType, _member);
}