using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Querying.Parameters;

/// <summary>
/// Describes a parameter member as seen from the root parameter object. A direct member has one path step.
/// A member reached through <see cref="NestedParametersAttribute"/> has several.
/// </summary>
public readonly struct ParameterMemberAccess {
    private readonly MemberInfo[] _path;
    private readonly LocalBuilder? _preparedValue;

    internal ParameterMemberAccess(Type rootType, MemberInfo[] path, LocalBuilder? preparedValue = null) {
        RootType = rootType;
        _path = path;
        _preparedValue = preparedValue;
    }

    /// <summary>The runtime parameter object's type.</summary>
    public Type RootType { get; }

    /// <summary>The final field/property that provides the parameter value.</summary>
    public MemberInfo Member => _path[^1];

    /// <summary>The final value type.</summary>
    public Type MemberType => GetMemberType(Member);

    /// <summary>The number of flattened object hops before the final member.</summary>
    public int Depth => _path.Length - 1;

    /// <summary>Whether the member is reached through at least one flattened object.</summary>
    public bool IsNested => _path.Length > 1;

    /// <summary>Emits the final typed value. Nested accesses are prepared/null-checked by the accessor generator.</summary>
    public void EmitLoad(ILGenerator il) {
        if (_preparedValue is not null) {
            il.Emit(OpCodes.Ldloc, _preparedValue);
            return;
        }
        if (_path.Length != 1)
            throw new InvalidOperationException("A nested parameter member must be prepared before its value is emitted.");
        AccessorEmitter.EmitMemberLoad(il, RootType, _path[0]);
    }

    /// <summary>Emits the final value and boxes value types.</summary>
    public void EmitValue(ILGenerator il) {
        EmitLoad(il);
        Type valueType = MemberType;
        if (valueType.IsValueType)
            il.Emit(OpCodes.Box, valueType);
    }

    internal ParameterMemberAccess Prepare(ILGenerator il, Label missing) {
        if (_path.Length == 1)
            return this;

        Type ownerType = RootType;
        LocalBuilder? owner = null;

        for (int i = 0; i < _path.Length; i++) {
            MemberInfo member = _path[i];
            if (i == 0)
                AccessorEmitter.EmitMemberLoad(il, RootType, member);
            else
                EmitMemberLoad(il, owner!, ownerType, member);

            Type valueType = GetMemberType(member);
            LocalBuilder value = il.DeclareLocal(valueType);
            il.Emit(OpCodes.Stloc, value);

            if (i == _path.Length - 1)
                return new ParameterMemberAccess(RootType, _path, value);

            if (!valueType.IsValueType) {
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Brfalse, missing);
                ownerType = valueType;
                owner = value;
                continue;
            }

            Type? nullableType = Nullable.GetUnderlyingType(valueType);
            if (nullableType is null) {
                ownerType = valueType;
                owner = value;
                continue;
            }

            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, valueType.GetProperty(nameof(Nullable<int>.HasValue))!.GetMethod!);
            il.Emit(OpCodes.Brfalse, missing);

            LocalBuilder unwrapped = il.DeclareLocal(nullableType);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Call, valueType.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, unwrapped);
            ownerType = nullableType;
            owner = unwrapped;
        }

        throw new InvalidOperationException("Invalid parameter member path.");
    }

    internal bool SamePath(ParameterMemberAccess other) {
        if (!ReferenceEquals(RootType, other.RootType) || _path.Length != other._path.Length)
            return false;
        for (int i = 0; i < _path.Length; i++)
            if (!Equals(_path[i], other._path[i]))
                return false;
        return true;
    }

    internal static Type GetMemberType(MemberInfo member) => member switch {
        FieldInfo field => field.FieldType,
        PropertyInfo property => property.PropertyType,
        _ => throw new ArgumentException("A parameter member must be a field or property.", nameof(member))
    };

    private static void EmitMemberLoad(ILGenerator il, LocalBuilder owner, Type ownerType, MemberInfo member) {
        if (member is FieldInfo field) {
            if (field.IsStatic) {
                il.Emit(OpCodes.Ldsfld, field);
                return;
            }
            il.Emit(ownerType.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, owner);
            il.Emit(OpCodes.Ldfld, field);
            return;
        }

        var property = (PropertyInfo)member;
        MethodInfo getter = property.GetMethod
            ?? throw new InvalidOperationException($"Parameter property '{property.Name}' has no getter.");
        if (getter.IsStatic) {
            il.Emit(OpCodes.Call, getter);
            return;
        }
        il.Emit(ownerType.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, owner);
        il.Emit(ownerType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, getter);
    }
}
