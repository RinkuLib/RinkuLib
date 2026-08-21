using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>
/// Cached shallow nested draft copier. It deliberately does not infer collection ownership or deep-clone arbitrary object graphs.
/// </summary>
public static class RuntimeNestedCopier<T>
{
    private static readonly ConcurrentDictionary<Type, ReferencePlan> Plans = new();

    /// <summary>Creates a shallow nested draft.</summary>
    public static T Clone(T value)
    {
        if (value is null || typeof(T).IsValueType || typeof(T) == typeof(string)) return value;
        if (value is Array array) return (T)(object)array.Clone();
        Type runtimeType = value.GetType();
        RejectCollection(runtimeType);
        return Plans.GetOrAdd(runtimeType, static type => new ReferencePlan(type)).Clone(value);
    }

    /// <summary>Returns whether a nested draft has changed.</summary>
    public static bool HasChanges(T original, T draft)
    {
        if (typeof(T).IsValueType || typeof(T) == typeof(string))
            return !EqualityComparer<T>.Default.Equals(original, draft);
        if (ReferenceEquals(original, draft)) return false;
        if (original is null || draft is null) return true;
        if (original.GetType() != draft.GetType()) return true;
        if (original is Array left && draft is Array right)
            return !StructuralComparisons.StructuralEqualityComparer.Equals(left, right);

        Type runtimeType = original.GetType();
        RejectCollection(runtimeType);
        return !Plans.GetOrAdd(runtimeType, static type => new ReferencePlan(type)).Equals(original, draft);
    }

    /// <summary>Copies changed nested members into the accepted object.</summary>
    public static void CopyInPlace(T original, T draft)
    {
        if (typeof(T).IsValueType || typeof(T) == typeof(string))
            throw new InvalidOperationException($"{typeof(T)} cannot be accepted as an in-place nested edit.");
        if (original is null || draft is null)
            throw new InvalidOperationException("An in-place nested edit cannot change a null reference. Use replacement mode.");
        if (original.GetType() != draft.GetType())
            throw new InvalidOperationException("An in-place nested edit cannot change the nested runtime type. Use replacement mode.");

        if (original is Array targetArray && draft is Array sourceArray)
        {
            if (targetArray.Rank != sourceArray.Rank || targetArray.Length != sourceArray.Length)
                throw new InvalidOperationException("An in-place nested array edit cannot change the array shape. Use replacement mode.");
            Array.Copy(sourceArray, targetArray, sourceArray.Length);
            return;
        }

        Type runtimeType = original.GetType();
        RejectCollection(runtimeType);
        Plans.GetOrAdd(runtimeType, static type => new ReferencePlan(type)).Copy(original, draft);
    }

    private static void RejectCollection(Type type)
    {
        if (typeof(IList).IsAssignableFrom(type))
            throw new NotSupportedException($"Nested draft cloning does not infer collection ownership for {type}. Configure replacement/custom copy behavior instead.");

        Type[] interfaces = type.GetInterfaces();
        for (int i = 0; i < interfaces.Length; i++)
        {
            Type current = interfaces[i];
            if (!current.IsGenericType) continue;
            Type definition = current.GetGenericTypeDefinition();
            if (definition == typeof(ICollection<>) || definition == typeof(IDictionary<,>))
                throw new NotSupportedException($"Nested draft cloning does not infer collection ownership for {type}. Configure replacement/custom copy behavior instead.");
        }
    }

    private sealed class ReferencePlan
    {
        private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(object).FullName, "MemberwiseClone");

        private readonly Func<T, T> _clone;
        private readonly Func<T, T, bool> _equals;
        private readonly Action<T, T> _copy;

        internal ReferencePlan(Type runtimeType)
        {
            MemberAccess[] members = GetReadableMembers(runtimeType);
            _clone = BuildClone(runtimeType);
            _equals = BuildEquals(runtimeType, members);
            _copy = BuildCopy(runtimeType, members);
        }

        internal T Clone(T source) => _clone(source);
        internal bool Equals(T left, T right) => _equals(left, right);
        internal void Copy(T target, T source) => _copy(target, source);

        private static Func<T, T> BuildClone(Type runtimeType)
        {
            var method = new DynamicMethod($"NestedClone_{runtimeType.Name}", typeof(T), [typeof(T)], typeof(RuntimeNestedCopier<T>).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtimeType);
            il.Emit(OpCodes.Call, MemberwiseCloneMethod);
            il.Emit(OpCodes.Castclass, typeof(T));
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<T, T>>();
        }

        private static Func<T, T, bool> BuildEquals(Type runtimeType, MemberAccess[] members)
        {
            var method = new DynamicMethod($"NestedEquals_{runtimeType.Name}", typeof(bool), [typeof(T), typeof(T)], typeof(RuntimeNestedCopier<T>).Module, true);
            ILGenerator il = method.GetILGenerator();
            Label different = il.DefineLabel();

            for (int i = 0; i < members.Length; i++)
            {
                MemberAccess member = members[i];
                Type comparer = typeof(EqualityComparer<>).MakeGenericType(member.Type);
                MethodInfo getDefault = comparer.GetProperty(nameof(EqualityComparer<int>.Default))?.GetMethod
                    ?? throw new MissingMethodException(comparer.FullName, $"get_{nameof(EqualityComparer<int>.Default)}");
                MethodInfo equals = comparer.GetMethod(nameof(EqualityComparer<int>.Equals), [member.Type, member.Type])
                    ?? throw new MissingMethodException(comparer.FullName, nameof(EqualityComparer<int>.Equals));

                il.Emit(OpCodes.Call, getDefault);
                EmitRead(il, runtimeType, member, 0);
                EmitRead(il, runtimeType, member, 1);
                il.Emit(OpCodes.Callvirt, equals);
                il.Emit(OpCodes.Brfalse, different);
            }

            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(different);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<T, T, bool>>();
        }

        private static Action<T, T> BuildCopy(Type runtimeType, MemberAccess[] members)
        {
            var method = new DynamicMethod($"NestedCopy_{runtimeType.Name}", typeof(void), [typeof(T), typeof(T)], typeof(RuntimeNestedCopier<T>).Module, true);
            ILGenerator il = method.GetILGenerator();

            for (int i = 0; i < members.Length; i++)
            {
                MemberAccess member = members[i];
                if (!member.CanWrite) continue;

                Label same = il.DefineLabel();
                Type comparer = typeof(EqualityComparer<>).MakeGenericType(member.Type);
                MethodInfo getDefault = comparer.GetProperty(nameof(EqualityComparer<int>.Default))?.GetMethod
                    ?? throw new MissingMethodException(comparer.FullName, $"get_{nameof(EqualityComparer<int>.Default)}");
                MethodInfo equals = comparer.GetMethod(nameof(EqualityComparer<int>.Equals), [member.Type, member.Type])
                    ?? throw new MissingMethodException(comparer.FullName, nameof(EqualityComparer<int>.Equals));

                il.Emit(OpCodes.Call, getDefault);
                EmitRead(il, runtimeType, member, 0);
                EmitRead(il, runtimeType, member, 1);
                il.Emit(OpCodes.Callvirt, equals);
                il.Emit(OpCodes.Brtrue, same);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, runtimeType);
                EmitRead(il, runtimeType, member, 1);
                if (member.Property is PropertyInfo property)
                {
                    MethodInfo setter = property.SetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"set_{property.Name}");
                    il.Emit(!setter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, setter);
                }
                else
                {
                    il.Emit(OpCodes.Stfld, member.Field ?? throw new InvalidOperationException("Nested member has neither property nor field."));
                }

                il.MarkLabel(same);
            }

            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Action<T, T>>();
        }

        private static void EmitRead(ILGenerator il, Type runtimeType, MemberAccess member, int argument)
        {
            il.Emit(argument == 0 ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtimeType);
            if (member.Property is PropertyInfo property)
            {
                MethodInfo getter = property.GetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"get_{property.Name}");
                il.Emit(!getter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, getter);
            }
            else
            {
                il.Emit(OpCodes.Ldfld, member.Field ?? throw new InvalidOperationException("Nested member has neither property nor field."));
            }
        }

        private static MemberAccess[] GetReadableMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var result = new List<MemberAccess>();
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true) continue;
                result.Add(new(property.PropertyType, property, null, property.SetMethod?.IsPublic == true));
            }
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.IsStatic) continue;
                result.Add(new(field.FieldType, null, field, !field.IsInitOnly && !field.IsLiteral));
            }
            return result.ToArray();
        }
    }

    private readonly record struct MemberAccess(Type Type, PropertyInfo? Property, FieldInfo? Field, bool CanWrite);
}
