using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Mapping;
internal static class TypeExtensions {
    internal static T Require<T>(T? value, string name) where T : class
        => value ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"Required reflection member {name} was not found.");
    internal static MethodInfo RequiredMethod(Type type, string name, Type[] parameterTypes)
        => type.GetMethod(name, parameterTypes)
            ?? throw new MissingMethodException(type.FullName, name);
    internal static ConstructorInfo RequiredConstructor(Type type, Type[] parameterTypes)
        => type.GetConstructor(parameterTypes)
            ?? throw new MissingMethodException(type.FullName, ".ctor");
    #region Debug
    internal static readonly MethodInfo WriteLine = RequiredMethod(typeof(Console), "WriteLine", [typeof(object)]);
    internal static void EmitWriteLineStackTop(this Type type, ILGenerator il) {
        il.Emit(OpCodes.Dup);
        if (type.IsValueType)
            il.Emit(OpCodes.Box, type);
        il.Emit(OpCodes.Call, WriteLine);
    }
    internal static void EmitWriteLineStack(this ILGenerator il, params Type[] types) {
        if (types == null || types.Length == 0)
            return;
        var locals = new LocalBuilder[types.Length];
        for (int i = types.Length - 1; i >= 0; i--) {
            locals[i] = il.DeclareLocal(types[i]);
            il.Emit(OpCodes.Stloc, locals[i]);
        }
        for (int i = 0; i < types.Length; i++) {
            il.Emit(OpCodes.Ldstr, $"[Stack Index {i}]: ");
            il.Emit(OpCodes.Call, WriteLine);
            il.Emit(OpCodes.Ldloc, locals[i]);
            if (types[i].IsValueType)
                il.Emit(OpCodes.Box, types[i]);
            il.Emit(OpCodes.Call, WriteLine);
            il.Emit(OpCodes.Ldloc, locals[i]);
        }
    }
    #endregion
    #region DbReaderVars
    internal static readonly Type[] IntParam = [typeof(int)];
    internal static readonly Type ReaderType = typeof(DbDataReader);
    internal static readonly MethodInfo IsNull = RequiredMethod(ReaderType, nameof(DbDataReader.IsDBNull), IntParam);
    
    internal static readonly MethodInfo _obj = RequiredMethod(ReaderType, nameof(DbDataReader.GetValue), IntParam);
    internal static readonly MethodInfo _str = RequiredMethod(ReaderType, nameof(DbDataReader.GetString), IntParam);
    internal static readonly MethodInfo _i8 = RequiredMethod(ReaderType, nameof(DbDataReader.GetByte), IntParam);
    internal static readonly MethodInfo _i16 = RequiredMethod(ReaderType, nameof(DbDataReader.GetInt16), IntParam);
    internal static readonly MethodInfo _i32 = RequiredMethod(ReaderType, nameof(DbDataReader.GetInt32), IntParam);
    internal static readonly MethodInfo _i64 = RequiredMethod(ReaderType, nameof(DbDataReader.GetInt64), IntParam);
    internal static readonly MethodInfo _char = RequiredMethod(ReaderType, nameof(DbDataReader.GetChar), IntParam);
    internal static readonly MethodInfo _bool = RequiredMethod(ReaderType, nameof(DbDataReader.GetBoolean), IntParam);
    internal static readonly MethodInfo _dt = RequiredMethod(ReaderType, nameof(DbDataReader.GetDateTime), IntParam);
    internal static readonly MethodInfo _dbl = RequiredMethod(ReaderType, nameof(DbDataReader.GetDouble), IntParam);
    internal static readonly MethodInfo _flt = RequiredMethod(ReaderType, nameof(DbDataReader.GetFloat), IntParam);
    internal static readonly MethodInfo _dec = RequiredMethod(ReaderType, nameof(DbDataReader.GetDecimal), IntParam);
    internal static readonly MethodInfo _guid = RequiredMethod(ReaderType, nameof(DbDataReader.GetGuid), IntParam);
    internal static readonly MethodInfo _getMeth = RequiredMethod(ReaderType, nameof(DbDataReader.GetFieldValue), IntParam);
    internal static readonly MethodInfo _byteArr = _getMeth.MakeGenericMethod(typeof(byte[]));
    
    internal static readonly ConstructorInfo _Ni8Ctor = RequiredConstructor(typeof(byte?), [typeof(byte)]);
    internal static readonly ConstructorInfo _Ni16Ctor = RequiredConstructor(typeof(short?), [typeof(short)]);
    internal static readonly ConstructorInfo _Ni32Ctor = RequiredConstructor(typeof(int?), [typeof(int)]);
    internal static readonly ConstructorInfo _Ni64Ctor = RequiredConstructor(typeof(long?), [typeof(long)]);
    internal static readonly ConstructorInfo _NcharCtor = RequiredConstructor(typeof(char?), [typeof(char)]);
    internal static readonly ConstructorInfo _NboolCtor = RequiredConstructor(typeof(bool?), [typeof(bool)]);
    internal static readonly ConstructorInfo _NdtCtor = RequiredConstructor(typeof(DateTime?), [typeof(DateTime)]);
    internal static readonly ConstructorInfo _NdblCtor = RequiredConstructor(typeof(double?), [typeof(double)]);
    internal static readonly ConstructorInfo _NfltCtor = RequiredConstructor(typeof(float?), [typeof(float)]);
    internal static readonly ConstructorInfo _NdecCtor = RequiredConstructor(typeof(decimal?), [typeof(decimal)]);
    internal static readonly ConstructorInfo _NguidCtor = RequiredConstructor(typeof(Guid?), [typeof(Guid)]);
    #endregion
    public static string ShortName(this Type? t) {
        if (t is null)
            return "void";
        if (!t.IsGenericType)
            return t.Name;

        var args = string.Join(", ", t.GetGenericArguments().Select(ShortName));
        string name = t.Name.Split('`')[0];
        return $"{name}<{args}>";
    }
    public static bool IsBaseType(this Type type) {
        return type == typeof(int) || type == typeof(string) || type == typeof(DateTime)
            || type == typeof(bool) || type == typeof(long) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(object) || type == typeof(float) || type == typeof(double)
            || type == typeof(char) || type == typeof(byte) || type == typeof(short) || type == typeof(byte[])
            || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong)
            || type == typeof(TimeSpan) || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
    public static bool IsNullable(this Type type)
        => !type.IsValueType || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
    public static MethodInfo GetDbMethod(this Type type) {
        var method = type switch {
            _ when type == typeof(int) => _i32,
            _ when type == typeof(string) => _str,
            _ when type == typeof(DateTime) => _dt,
            _ when type == typeof(bool) => _bool,
            _ when type == typeof(long) => _i64,
            _ when type == typeof(decimal) => _dec,
            _ when type == typeof(Guid) => _guid,
            _ when type == typeof(object) => _obj,
            _ when type == typeof(float) => _flt,
            _ when type == typeof(double) => _dbl,
            _ when type == typeof(char) => _char,
            _ when type == typeof(byte) => _i8,
            _ when type == typeof(short) => _i16,
            _ when type == typeof(byte[]) => _byteArr,
            _ => null
        };

        if (method is not null)
            return method;

        Type nullableType = type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type;
        return _getMeth.MakeGenericMethod(nullableType);
    }
    public static ConstructorInfo GetNullableConstructor(this Type type) {
        if (!type.IsValueType)
            throw new ArgumentException($"{type} must be a value type to have a Nullable<> constructor", nameof(type));
        var ctor = type switch {
            _ when type == typeof(int) => _Ni32Ctor,
            _ when type == typeof(DateTime) => _NdtCtor,
            _ when type == typeof(long) => _Ni64Ctor,
            _ when type == typeof(decimal) => _NdecCtor,
            _ when type == typeof(double) => _NdblCtor,
            _ when type == typeof(float) => _NfltCtor,
            _ when type == typeof(bool) => _NboolCtor,
            _ when type == typeof(Guid) => _NguidCtor,
            _ when type == typeof(char) => _NcharCtor,
            _ when type == typeof(byte) => _Ni8Ctor,
            _ when type == typeof(short) => _Ni16Ctor,
            _ => null
        };
        if (ctor is not null)
            return ctor;
        return typeof(Nullable<>).MakeGenericType(type).GetConstructor([type])
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"Could not find Nullable constructor for {type.Name}");
    }
    public static bool IsStackEquivalent(this Type source, Type target) {
        if (source == target)
            return true;
        if (!source.IsValueType && !target.IsValueType)
            return target.IsAssignableFrom(source);
        Type s = source.IsEnum ? Enum.GetUnderlyingType(source) : source;
        Type t = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
        return GetStackSlot(s) == GetStackSlot(t) && GetStackSlot(s) != null;
    }
    private static string? GetStackSlot(Type t) {
        switch (Type.GetTypeCode(t)) {
            case TypeCode.Boolean:
            case TypeCode.Char:
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
                return "int32";
            case TypeCode.Int64:
            case TypeCode.UInt64:
                return "int64";
            case TypeCode.Single:
                return "F4";
            case TypeCode.Double:
                return "F8";
            default:
                if (t == typeof(IntPtr) || t == typeof(UIntPtr) || t.IsPointer)
                    return "native_int";
                return null;
        }
    }
    public static MemberInfo GetClosedMember(this MemberInfo member, Type closedType) {
        closedType = Nullable.GetUnderlyingType(closedType) ?? closedType;
        if (!closedType.IsGenericType)
            return member;
        if (member is FieldInfo fi)
            return closedType.GetMemberWithSameMetadataDefinitionAs(fi);
        if (member is ConstructorInfo ci)
            return ConstructorInfo.GetMethodFromHandle(ci.MethodHandle, closedType.TypeHandle)
                ?? throw new MissingMethodException(closedType.FullName, ci.Name);
        if (member is PropertyInfo pi)
            member = pi.GetSetMethod(true) ?? throw new RinkuConfigurationException(ErrorCodes.UnusableMember, "Property has no setter");
        if (member is not MethodInfo mi)
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Member type {member.GetType()} not supported");
        if (!mi.ContainsGenericParameters)
            return mi;
        if (mi.IsGenericMethod)
            return mi.MakeGenericMethod(closedType.GetGenericArguments());
        return MethodBase.GetMethodFromHandle(mi.MethodHandle, closedType.TypeHandle)
            ?? throw new MissingMethodException(closedType.FullName, mi.Name);
    }
    public static Type CloseType(this Type type, Type closedParent) {
        if (!type.ContainsGenericParameters)
            return type;
        Type[] parentArgs = closedParent.GetGenericArguments();
        return Resolve(type, parentArgs);
    }
    public static Type CloseType(this Type type, Type[] parentTypeArguments) {
        if (!type.ContainsGenericParameters)
            return type;
        return Resolve(type, parentTypeArguments);
    }

    private static Type Resolve(Type target, Type[] parentArgs) {
        if (target.IsGenericParameter)
            return parentArgs[target.GenericParameterPosition];
        if (target.IsGenericType) {
            Type[] targetArgs = target.GetGenericArguments();
            bool changed = false;
            for (int i = 0; i < targetArgs.Length; i++) {
                Type resolved = Resolve(targetArgs[i], parentArgs);
                if (resolved != targetArgs[i]) {
                    targetArgs[i] = resolved;
                    changed = true;
                }
            }

            return changed ? target.GetGenericTypeDefinition().MakeGenericType(targetArgs) : target;
        }
        if (target.IsArray) {
            Type elementType = target.GetElementType() ?? throw new InvalidOperationException($"{target} has no element type");
            Type resolvedElement = Resolve(elementType, parentArgs);

            if (resolvedElement != elementType) {
                int rank = target.GetArrayRank();
                return rank > 1
                    ? resolvedElement.MakeArrayType(rank)
                    : resolvedElement.MakeArrayType();
            }
            return target;
        }

        if (target.IsPointer) {
            Type elementType = target.GetElementType() ?? throw new InvalidOperationException($"{target} has no element type");
            Type resolved = Resolve(elementType, parentArgs);
            return resolved != elementType ? resolved.MakePointerType() : target;
        }

        if (target.IsByRef) {
            Type elementType = target.GetElementType() ?? throw new InvalidOperationException($"{target} has no element type");
            Type resolved = Resolve(elementType, parentArgs);
            return resolved != elementType ? resolved.MakeByRefType() : target;
        }

        return target;
    }
    public static bool IsTypeDefault(this ParameterInfo p) {
        if (!p.HasDefaultValue)
            return false;

        object? value = p.DefaultValue;
        Type type = p.ParameterType;

        if (value == null || value == DBNull.Value)
            return true;

        Type actualType = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

        try {
            switch (Type.GetTypeCode(actualType)) {
                case TypeCode.Boolean:
                    return value is bool b && !b;
                case TypeCode.Char:
                    return value is char c && c == '\0';
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return Convert.ToInt64(value) == 0;
                case TypeCode.Single:
                case TypeCode.Double:
                    return Convert.ToDouble(value) == 0.0;
                case TypeCode.Decimal:
                    return Convert.ToDecimal(value) == 0m;
                case TypeCode.DateTime:
                    return value is DateTime dt && dt == default;

                case TypeCode.Object:
                    if (actualType == typeof(Guid))
                        return value is Guid g && g == Guid.Empty;
                    if (actualType == typeof(TimeSpan))
                        return value is TimeSpan ts && ts == TimeSpan.Zero;
                    if (actualType == typeof(DateTimeOffset))
                        return value is DateTimeOffset dto && dto == default;
                    return value.Equals(Activator.CreateInstance(actualType));

                default:
                    return false;
            }
        }
        catch {
            return false;
        }
    }
}
