using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.DbParsing;
/// <summary>
/// Provides utilities for IL generation, specifically for debugging the evaluation stack 
/// and handling database-specific method resolution.
/// </summary>
public static class TypeExtensions {
    #region Debug
    /// <inheritdoc/>
    public static readonly MethodInfo WriteLine = typeof(Console).GetMethod("WriteLine", [typeof(object)])!;
    /// <summary>
    /// Emits IL to print the current value at the top of the stack to the Console.
    /// Automatically boxes value types to ensure compatibility with <see cref="Console.WriteLine(object)"/>.
    /// </summary>
    public static void EmitWriteLineStackTop(this Type type, ILGenerator il) {
        il.Emit(OpCodes.Dup);
        if (type.IsValueType)
            il.Emit(OpCodes.Box, type);
        il.Emit(OpCodes.Call, WriteLine);
    }
    /// <summary>
    /// Inspects and prints multiple items from the stack by temporarily storing them in locals.
    /// Useful for verifying stack state during complex multi-parameter object construction.
    /// </summary>
    public static void EmitWriteLineStack(this ILGenerator il, params Type[] types) {
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
    internal static readonly MethodInfo IsNull = ReaderType.GetMethod(nameof(DbDataReader.IsDBNull), IntParam)!;
    
    internal static readonly MethodInfo _obj = ReaderType.GetMethod(nameof(DbDataReader.GetValue), IntParam)!;
    internal static readonly MethodInfo _str = ReaderType.GetMethod(nameof(DbDataReader.GetString), IntParam)!;
    internal static readonly MethodInfo _i8 = ReaderType.GetMethod(nameof(DbDataReader.GetByte), IntParam)!;
    internal static readonly MethodInfo _i16 = ReaderType.GetMethod(nameof(DbDataReader.GetInt16), IntParam)!;
    internal static readonly MethodInfo _i32 = ReaderType.GetMethod(nameof(DbDataReader.GetInt32), IntParam)!;
    internal static readonly MethodInfo _i64 = ReaderType.GetMethod(nameof(DbDataReader.GetInt64), IntParam)!;
    internal static readonly MethodInfo _char = ReaderType.GetMethod(nameof(DbDataReader.GetChar), IntParam)!;
    internal static readonly MethodInfo _bool = ReaderType.GetMethod(nameof(DbDataReader.GetBoolean), IntParam)!;
    internal static readonly MethodInfo _dt = ReaderType.GetMethod(nameof(DbDataReader.GetDateTime), IntParam)!;
    internal static readonly MethodInfo _dbl = ReaderType.GetMethod(nameof(DbDataReader.GetDouble), IntParam)!;
    internal static readonly MethodInfo _flt = ReaderType.GetMethod(nameof(DbDataReader.GetFloat), IntParam)!;
    internal static readonly MethodInfo _dec = ReaderType.GetMethod(nameof(DbDataReader.GetDecimal), IntParam)!;
    internal static readonly MethodInfo _guid = ReaderType.GetMethod(nameof(DbDataReader.GetGuid), IntParam)!;
    internal static readonly MethodInfo _getMeth = ReaderType.GetMethod(nameof(DbDataReader.GetFieldValue), IntParam)!;
    
    internal static readonly ConstructorInfo _Ni8Ctor = typeof(byte?).GetConstructor([typeof(byte)])!;
    internal static readonly ConstructorInfo _Ni16Ctor = typeof(short?).GetConstructor([typeof(short)])!;
    internal static readonly ConstructorInfo _Ni32Ctor = typeof(int?).GetConstructor([typeof(int)])!;
    internal static readonly ConstructorInfo _Ni64Ctor = typeof(long?).GetConstructor([typeof(long)])!;
    internal static readonly ConstructorInfo _NcharCtor = typeof(char?).GetConstructor([typeof(char)])!;
    internal static readonly ConstructorInfo _NboolCtor = typeof(bool?).GetConstructor([typeof(bool)])!;
    internal static readonly ConstructorInfo _NdtCtor = typeof(DateTime?).GetConstructor([typeof(DateTime)])!;
    internal static readonly ConstructorInfo _NdblCtor = typeof(double?).GetConstructor([typeof(double)])!;
    internal static readonly ConstructorInfo _NfltCtor = typeof(float?).GetConstructor([typeof(float)])!;
    internal static readonly ConstructorInfo _NdecCtor = typeof(decimal?).GetConstructor([typeof(decimal)])!;
    internal static readonly ConstructorInfo _NguidCtor = typeof(Guid?).GetConstructor([typeof(Guid)])!;
    #endregion
    /// <summary>Get a human readable type name with generic</summary>
    public static string ShortName(this Type? t) {
        if (t is null)
            return "void";
        if (!t.IsGenericType)
            return t.Name;

        // Formats Reference`2[[Int32...]] into Reference<Int32, String>
        var args = string.Join(", ", t.GetGenericArguments().Select(ShortName));
        string name = t.Name.Split('`')[0];
        return $"{name}<{args}>";
    }
    /// <summary>
    /// Check if the type has a related method in <see cref="DbDataReader"/>.
    /// </summary>
    public static bool IsBaseType(this Type type) {
        return type == typeof(int) || type == typeof(string) || type == typeof(DateTime)
            || type == typeof(bool) || type == typeof(long) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(object) || type == typeof(float)
            || type == typeof(double) || type == typeof(char) || type == typeof(byte) || type == typeof(short);
    }
    /// <summary>
    /// Check if the type can hold a <see langword="null"/> value
    /// </summary>
    public static bool IsNullable(this Type type)
        => !type.IsValueType || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
    private readonly static Dictionary<Type, MethodInfo> NotNullGetter = [];
    /// <summary>
    /// Get the <see cref="DbDataReader"/> method for a given Type.
    /// </summary>
    /// <remarks>
    /// If a specialized method (like GetInt32) does not exist for the type, 
    /// it falls back to the generic <see cref="DbDataReader.GetFieldValue{T}"/>.
    /// </remarks>
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
            _ => null
        };

        if (method is not null)
            return method;

        lock (NotNullGetter) {
            if (NotNullGetter.TryGetValue(type, out var cached))
                return cached;
            Type nullableType = type.IsValueType
                ? typeof(Nullable<>).MakeGenericType(type)
                : type;
            var result = _getMeth.MakeGenericMethod(nullableType);
            NotNullGetter[type] = result;
            return result;
        }
    }
    /// <summary>
    /// Retrieves the constructor for <see cref="Nullable{T}"/> for the given value type.
    /// </summary>
    public static ConstructorInfo GetNullableConstructor(this Type type) {
        if (!type.IsValueType)
            throw new Exception("type must be a value type in order to have a nullable ctor");
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
                ?? throw new Exception($"Could not find Nullable constructor for {type.Name}");
    }
    /// <summary>
    /// Determines if two types share the same representation on the CIL evaluation stack.
    /// </summary>
    /// <example>
    /// <c>int</c> and <c>DayOfWeek</c> (Enum) are stack equivalent as both occupy an int32 slot.
    /// </example>
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
    /// <summary>
    /// Maps a member (Field, Property, Ctor) from an open generic definition 
    /// to its equivalent on a specific closed generic type.
    /// </summary>
    public static MemberInfo GetClosedMember(this MemberInfo member, Type closedType) {
        closedType = Nullable.GetUnderlyingType(closedType) ?? closedType;
        if (!closedType.IsGenericType)
            return member;
        if (member is FieldInfo fi)
            return FieldInfo.GetFieldFromHandle(fi.FieldHandle, closedType.TypeHandle);
        if (member is ConstructorInfo ci)
            return ConstructorInfo.GetMethodFromHandle(ci.MethodHandle, closedType.TypeHandle)!;
        if (member is PropertyInfo pi)
            member = pi.GetSetMethod(true) ?? throw new InvalidOperationException("Property has no setter");
        if (member is not MethodInfo mi)
            throw new NotImplementedException($"Member type {member.GetType()} not supported.");
        if (mi.IsGenericMethod)
            return mi.MakeGenericMethod(closedType.GetGenericArguments());
        return MethodBase.GetMethodFromHandle(mi.MethodHandle, closedType.TypeHandle)!;
    }
    /// <summary>
    /// Checks if a database-sourced type can be logically converted to a target C# type 
    /// without explicit transformation logic.
    /// </summary>
    public static bool CanConvert(this Type dbType, Type target) {
        Type targetCore = Nullable.GetUnderlyingType(target) ?? target;
        if (targetCore.IsAssignableFrom(dbType))
            return true;
        if (targetCore.IsEnum)
            return dbType.IsPrimitive;
        bool isTargetNumeric = targetCore.IsPrimitive || targetCore == typeof(decimal);
        bool isSourceNumeric = dbType.IsPrimitive || dbType == typeof(decimal);

        return isTargetNumeric && isSourceNumeric;
    }
    /// <summary>
    /// Replaces generic placeholders (T, TKey) with actual types from the parent closed type.
    /// </summary>
    /// <param name="type">The member type that might be open (e.g., <see cref="List{T}"/>)</param>
    /// <param name="closedParent">The closed provider (e.g., <see cref="KeyValuePair{Int32, String}"/>)</param>
    public static Type CloseType(this Type type, Type closedParent) {
        if (!type.ContainsGenericParameters)
            return type;
        Type[] parentArgs = closedParent.GetGenericArguments();
        return Resolve(type, parentArgs);
    }
    /// <summary>
    /// Resolves generic placeholders (T, TKey) within a type using the 
    /// generic arguments provided by a parent context.
    /// </summary>
    /// <param name="type">The type containing open parameters.</param>
    /// <param name="parentTypeArguments">The actual types to substitute.</param>
    /// <returns>A fully closed, executable <see cref="Type"/>.</returns>
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
            Type elementType = target.GetElementType()!;
            Type resolved = Resolve(elementType, parentArgs);
            return resolved != elementType ? resolved.MakeArrayType() : target;
        }

        return target;
    }
}