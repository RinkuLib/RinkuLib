using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Identifies a member as a boolean condition for SQL templates rather than a variable.
/// </summary>
/// <remarks>
/// <b>Note:</b> Only valid on <see cref="bool"/> fields or properties. 
/// If applied to other types, it is silently ignored and treated as a standard member.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ForBoolCondAttribute : Attribute;
/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly ref struct TypeAccessor(object item, Func<object, int, bool> usage, Func<object, int, object> value) {
    private readonly object _item = item;
    private readonly Func<object, int, bool> _getUsage = usage;
    private readonly Func<object, int, object> _getValue = value;
    /// <summary>
    /// Check if the value is used
    /// </summary>
    public bool IsUsed(int index) => _getUsage(_item, index);
    /// <summary>
    /// Get the used value
    /// </summary>
    public object GetValue(int index) => _getValue(_item, index);
}
/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly ref struct TypeAccessor<T>(ref T item, MemberUsageDelegate<T> usage, MemberValueDelegate<T> value) {
    private readonly ref T _item = ref item;
    private readonly MemberUsageDelegate<T> _getUsage = usage;
    private readonly MemberValueDelegate<T> _getValue = value;
    /// <summary>
    /// Check if the value is used
    /// </summary>
    public bool IsUsed(int index) => _getUsage(ref _item, index);
    /// <summary>
    /// Get the used value
    /// </summary>
    public object GetValue(int index) => _getValue(ref _item, index);
}
/// <summary>
/// Represent a il compiled delegate to get the usage and value of a type based on a mapper
/// </summary>
public class TypeAccessorCache {
    /// <summary>The delegate to get the usage</summary>
    public Func<object, int, bool> GetUsage;
    /// <summary>The delegate to get the value</summary>
    public Func<object, int, object> GetValue;
    /// <inheritdoc/>
    protected TypeAccessorCache() {
        GetUsage = default!;
        GetValue = default!;
    }
    /// <inheritdoc/>
    public TypeAccessorCache(DynamicMethod usageMethod, DynamicMethod valueMethod) {
        this.GetUsage = usageMethod.CreateDelegate<Func<object, int, bool>>();
        this.GetValue = valueMethod.CreateDelegate<Func<object, int, object>>();
    }
}
/// <summary>
/// Fast delegate to switch for usage
/// </summary>
public delegate bool MemberUsageDelegate<T>(ref T instance, int index);
/// <summary>
/// Fast delegate to switch for value
/// </summary>
public delegate object MemberValueDelegate<T>(ref T instance, int index);
/// <summary>
/// Represent a generic il compiled delegate to get the usage and value of a type based on a mapper
/// </summary>
public class StructTypeAccessorCache<T> : TypeAccessorCache {
    /// <summary>The generic delegate to get the value</summary>
    public MemberUsageDelegate<T> GenericGetUsage;
    /// <summary>The generic delegate to get the value</summary>
    public MemberValueDelegate<T> GenericGetValue;
    /// <inheritdoc/>
    public StructTypeAccessorCache(DynamicMethod usageMethod, DynamicMethod valueMethod) {
        this.GenericGetUsage = usageMethod.CreateDelegate<MemberUsageDelegate<T>>();
        this.GenericGetValue = valueMethod.CreateDelegate<MemberValueDelegate<T>>();
        this.GetUsage = CreateBoxedWrapper<bool>(usageMethod);
        this.GetValue = CreateBoxedWrapper<object>(valueMethod);
    }

    private static Func<object, int, TReturn> CreateBoxedWrapper<TReturn>(DynamicMethod internalMethod) {
        var wrapper = new DynamicMethod($"BoxedWrapper_{internalMethod.Name}", typeof(TReturn), 
            [typeof(object), typeof(int)], typeof(T).Module, skipVisibility: true);
        ILGenerator il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, internalMethod);
        il.Emit(OpCodes.Ret);

        return wrapper.CreateDelegate<Func<object, int, TReturn>>();
    }
} 
/// <summary>
/// IL compiled access of <typeparamref name="T"/>
/// </summary>
public static class TypeAccessorCacher<T> {
    /// <summary>
    /// A lock shared to ensure thread safety across multiple <see cref="TypeAccessor"/> instances.
    /// </summary>
    public static readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        SharedLock = new();
    private static (object Key, TypeAccessorCache Cache)[] Variants = [];
    /// <summary>
    /// Get the compiled accesor
    /// </summary>
    public static TypeAccessorCache GetOrGenerate(Mapper mapper) {
        var currentVariants = Variants;
        foreach (var (Keys, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper))
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, Cache) in Variants)
                if (ReferenceEquals(Keys, mapper))
                    return Cache;
            var firstKey = mapper.Count > 0 ? mapper.Keys[0] : default;
            var varChar = string.IsNullOrEmpty(firstKey) ? default : firstKey[0];
            TypeAccessorCache cache = typeof(T).IsValueType
                ? new StructTypeAccessorCache<T>(GenerateDelegate(varChar, mapper, true), GenerateDelegate(varChar, mapper, false))
                : new TypeAccessorCache(GenerateDelegate(varChar, mapper, true), GenerateDelegate(varChar, mapper, false));

            Variants = [.. Variants, (mapper, cache)];
            return cache;
        }
    }
    private static DynamicMethod GenerateDelegate(char varChar, Mapper mapper, bool forUsage) {
        Type type = typeof(T);
        Type arg0 = type.IsValueType ? type.MakeByRefType() : typeof(object);
        DynamicMethod dm = new($"{type.Name}_{(forUsage ? "U" : "V")}", forUsage ? typeof(bool) : typeof(object), [arg0, typeof(int)], type.Module, true);
        var il = dm.GetILGenerator();

        int switchCount = mapper.Count;
        AccessorEmitter?[] plans = new AccessorEmitter?[switchCount];
        Label[] switchTable = new Label[switchCount];
        Label defaultLabel = il.DefineLabel();

        for (int i = 0; i < switchCount; i++)
            switchTable[i] = defaultLabel;

        MemberInfo[] allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        int index;
        foreach (var member in allMembers) {
            if (member is not FieldInfo && member is not PropertyInfo)
                continue;
            Type? memberType = 
                member is FieldInfo f ? f.FieldType : 
                member is PropertyInfo p ? p.PropertyType : 
                member is MethodInfo m ? m.ReturnType :
                null;
            if (memberType == typeof(bool) &&
                              member.IsDefined(typeof(ForBoolCondAttribute), inherit: true)) {
                index = mapper.GetIndex(member.Name);
                if (index < 0 || index >= switchCount)
                    continue;
                plans[index] = forUsage
                    ? new MemberCondUsageEmitter(type, member)
                    : new MemberValueEmitter(type, member);
                switchTable[index] = il.DefineLabel();
                continue;
            }
            index = GetIndexAppendVarChar(varChar, mapper, member);
            if (index < 0 || index >= switchCount)
                continue;
            plans[index] = forUsage
                ? new MemberUsageEmitter(type, member)
                : new MemberValueEmitter(type, member);
            switchTable[index] = il.DefineLabel();
        }

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Switch, switchTable);

        il.MarkLabel(defaultLabel);
        il.Emit(forUsage ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        for (int i = 0; i < switchCount; i++) {
            ref var plan = ref plans[i];
            var label = switchTable[i];
            if (label == defaultLabel || plan is null)
                continue;

            il.MarkLabel(label);
            plan.Emit(il);
            il.Emit(OpCodes.Ret);
        }
        return dm;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetIndexAppendVarChar(char variableChar, Mapper mapper, MemberInfo member) {
        string name = member.Name;
        Span<char> nameSpan = stackalloc char[name.Length + 1];
        nameSpan[0] = variableChar;
        name.AsSpan().CopyTo(nameSpan[1..]);
        return mapper.GetIndex(nameSpan);
    }
}