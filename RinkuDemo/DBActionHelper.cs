using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.DBActions;
using RinkuLib.Tools;

namespace RinkuDemo; 
public class DBActionHelper {
    public const char UsingSchemaPrependChar = '$';

    public static (string, int[], DbAction<T>) MakeAction<T>(Mapper mapper, string actionName) {
        return new(actionName, GetCondIndexes(actionName, mapper), CreateAction<T>(actionName));
    }
    public static string[] GetStrippedActions(Mapper mapper) {
        int count = 0;
        foreach (var key in mapper.Keys) 
            if (key.Length > 0 && key[0] == UsingSchemaPrependChar)
                count++;

        string[] result = new string[count];
        int index = 0;
        foreach (var key in mapper.Keys) 
            if (key.Length > 0 && key[0] == UsingSchemaPrependChar) 
                result[index++] = key.AsSpan(1).ToString();

        return result;
    }
    public static int[] GetCondIndexes(ReadOnlySpan<char> input, Mapper mapper) {
        Span<char> buffer = stackalloc char[input.Length + 1];
        Span<int> resultBuffer = stackalloc int[32];
        int count = 0;
        buffer[0] = UsingSchemaPrependChar;
        input.CopyTo(buffer[1..]);
        int fullIdx = mapper.GetIndex(buffer);
        if (fullIdx != -1)
            resultBuffer[count++] = fullIdx;
        buffer[0] = '.';
        int startAfterDot = 1;
        while (startAfterDot < buffer.Length) {
            int nextDot = buffer[startAfterDot..].IndexOf('.') + 1;
            if (nextDot == 0)
                nextDot = buffer.Length;
            var idx = mapper.GetIndex(buffer[(startAfterDot - 1)..nextDot]);
            if (idx != -1)
                resultBuffer[count++] = idx;
            startAfterDot = nextDot + 1;
        }

        return resultBuffer[..count].ToArray();
    }
    public static DbAction<T> CreateAction<T>(ReadOnlySpan<char> pattern) {
        int segmentCount = 1;
        foreach (char c in pattern)
            if (c == '.')
                segmentCount++;

        return (DbAction<T>)(segmentCount switch {
            1 => BuildOneLevelRelation(typeof(T), pattern.ToString()),
            2 => BuildTwoLevelRelation(typeof(T), pattern),
            _ => throw new Exception("Only supports 2 level")
        });
    }
    private static object BuildOneLevelRelation(Type parentType, string memberName) {
        var (colMember, colType, itemType) = GetCollectionMember(parentType, memberName);
        var (idMember, idType) = GetIdMember(parentType);

        var relationType = typeof(ToManyRelation<,,>).MakeGenericType(parentType, idType, itemType);

        return Activator.CreateInstance(relationType,
            CreateGetterIL(parentType, idType, idMember),
            CreateSetterIL(parentType, itemType, colMember, colType))!;
    }
    private static object BuildTwoLevelRelation(Type parentType, ReadOnlySpan<char> pattern) {
        int dotIndex = pattern.IndexOf('.');
        return BuildTwoLevelRelation(parentType, pattern[..dotIndex].ToString(), pattern[(dotIndex + 1)..].ToString());
    }
    private static object BuildTwoLevelRelation(Type parentType, string memberName, string nestedMemberName) {
        var (m1, t1, transientType) = GetCollectionMember(parentType, memberName);
        var (idMember, idType) = GetIdMember(parentType);

        var (m2, t2, itemType) = GetCollectionMember(transientType, nestedMemberName);
        var (idTransientMember, idTransientType) = GetIdMember(transientType);

        Type accessorType = t1.IsArray
            ? typeof(ArrayAccess<>).MakeGenericType(transientType)
            : typeof(ListAccess<>).MakeGenericType(transientType);

        var relationType = typeof(ToManyRelationTwoLevel<,,,,,>)
            .MakeGenericType(parentType, idType, transientType, idTransientType, itemType, accessorType);

        return Activator.CreateInstance(relationType,
            CreateGetterIL(parentType, idType, idMember),
            CreateAccessorGetterIL(parentType, accessorType, m1, t1),
            CreateGetterIL(transientType, idTransientType, idTransientMember),
            CreateSetterIL(transientType, itemType, m2, t2))!;
    }
    private static object CreateGetterIL(Type type, Type idType, MemberInfo member) {
        var method = new DynamicMethod($"GetID_{type.Name}", idType, [type.MakeByRefType()], type.Module, true);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        if (!type.IsValueType) il.Emit(OpCodes.Ldind_Ref);
        if (member is PropertyInfo prop) {
            var getMethod = prop.GetGetMethod(true)!;
            il.Emit(getMethod.IsVirtual && !type.IsValueType ? OpCodes.Callvirt : OpCodes.Call, getMethod);
        }
        else
            il.Emit(OpCodes.Ldfld, (FieldInfo)member);

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(typeof(Getter<,>).MakeGenericType(type, idType));
    }
    private static object CreateAccessorGetterIL(Type type, Type accessorType, MemberInfo member, Type memberType) {
        var method = new DynamicMethod($"GetAccessor_{type.Name}_{member.Name}", accessorType, [type.MakeByRefType()], type.Module, true);
        var il = method.GetILGenerator();
        var ctor = accessorType.GetConstructor([memberType])!;

        il.Emit(OpCodes.Ldarg_0);
        if (!type.IsValueType) il.Emit(OpCodes.Ldind_Ref);
        if (member is PropertyInfo prop) {
            var getMethod = prop.GetGetMethod(true)!;
            il.Emit(getMethod.IsVirtual && !type.IsValueType ? OpCodes.Callvirt : OpCodes.Call, getMethod);
        }
        else 
            il.Emit(OpCodes.Ldfld, (FieldInfo)member);

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);

        return method.CreateDelegate(typeof(Getter<,>).MakeGenericType(type, accessorType));
    }
    private static object CreateSetterIL(Type type, Type itemType, MemberInfo member, Type memberType) {
        Type pooledType = typeof(PooledArray<>).MakeGenericType(itemType);
        DynamicMethod method = new($"SetColl_{type.Name}_{member.Name}", null, [type.MakeByRefType(), pooledType], type.Module, true);
        ILGenerator il = method.GetILGenerator();

        string convName = memberType.IsArray
            ? nameof(PooledArray<>.ToArray)
            : nameof(PooledArray<>.ToList);

        il.Emit(OpCodes.Ldarg_0);
        if (!type.IsValueType) il.Emit(OpCodes.Ldind_Ref);
        il.Emit(OpCodes.Ldarga, 1);
        il.Emit(OpCodes.Call, pooledType.GetMethod(convName, Type.EmptyTypes)!);

        if (member is PropertyInfo prop) {
            var getMethod = prop.GetSetMethod(true)!;
            il.Emit(getMethod.IsVirtual && !type.IsValueType ? OpCodes.Callvirt : OpCodes.Call, getMethod);
        }
        else
            il.Emit(OpCodes.Stfld, (FieldInfo)member);

        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(typeof(Setter<,>).MakeGenericType(type, pooledType));
    }
    private static (MemberInfo Member, Type MemberType, Type ItemType) GetCollectionMember(Type type, string memberName) {
        MemberInfo member;
        Type memberType;

        var prop = type.GetProperty(memberName);
        if (prop is not null) {
            member = prop;
            memberType = prop.PropertyType;
        }
        else {
            var field = type.GetField(memberName) ?? throw new Exception($"Member '{memberName}' not found as Property or Field on type {type.Name}");
            member = field;
            memberType = field.FieldType;
        }

        Type? itemType = null;
        if (memberType.IsArray)
            itemType = memberType.GetElementType();
        else if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(List<>))
            itemType = memberType.GetGenericArguments()[0];

        if (itemType is null)
            throw new Exception($"Member '{memberName}' on {type.Name} is type {memberType.Name}. It must be an Array or List<T>.");

        return (member, memberType, itemType);
    }
    private static (MemberInfo Member, Type Type) GetIdMember(Type type) {
        var prop = type.GetProperty("ID");
        if (prop is not null)
            return (prop, prop.PropertyType);
        var field = type.GetField("ID");
        if (field is not null)
            return (field, field.FieldType);
        throw new Exception($"ID member (Property or Field) not found on type {type.Name}");
    }
}
