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
public readonly unsafe ref struct TypeAccessor(void* item, MemberUsageDelegate usage, MemberValueDelegate value) {
    // Store as void* to match the delegate signature perfectly
    private readonly void* _item = item;
    private readonly MemberUsageDelegate _getUsage = usage;
    private readonly MemberValueDelegate _getValue = value;
    /// <summary>
    /// Check if the value is used
    /// </summary>
    public bool IsUsed(int index) => _getUsage(_item, index);
    /// <summary>
    /// Get the used value
    /// </summary>
    public object GetValue(int index) => _getValue(_item, index);
#if DEBUG
#pragma warning disable CA2211
    /// <summary></summary>
    public static Action<string> Write = Console.WriteLine;
#pragma warning restore CA2211
#endif
}
/// <summary>
/// Fast unsafe delegate to switch for usage
/// </summary>
public unsafe delegate bool MemberUsageDelegate(void* instance, int index);
/// <summary>
/// Fast unsafe delegate to switch for value
/// </summary>
public unsafe delegate object MemberValueDelegate(void* instance, int index);
/// <summary>
/// IL compiled access of <typeparamref name="T"/>
/// </summary>
public static class TypeAccessor<T> {
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
    private static (object Key, MemberUsageDelegate Usage, MemberValueDelegate Value)[] Variants = [];
    /// <summary>
    /// Get the compiled accesor
    /// </summary>
    public static (MemberUsageDelegate Usage, MemberValueDelegate Value) GetOrGenerate(Mapper mapper) {
        var currentVariants = Variants;
        foreach (var (Keys, Usage, Value) in currentVariants)
            if (ReferenceEquals(Keys, mapper))
                return (Usage, Value);

        lock (SharedLock) {
            foreach (var (Keys, Usage, Value) in Variants)
                if (ReferenceEquals(Keys, mapper))
                    return (Usage, Value);
            var firstKey = mapper.Count > 0 ? mapper.Keys[0] : default;
            var varChar = string.IsNullOrEmpty(firstKey) ? default : firstKey[0];
            var usage = GenerateDelegate<MemberUsageDelegate>(varChar, mapper, true);
            var value = GenerateDelegate<MemberValueDelegate>(varChar, mapper, false);

            Variants = [.. Variants, (mapper, usage, value)];
            return (usage, value);
        }
    }
    private static TDelegate GenerateDelegate<TDelegate>(char varChar, Mapper mapper, bool forUsage) where TDelegate : Delegate {
        Type type = typeof(T);
        DynamicMethod dm = new($"{type.Name}_{(forUsage ? "U" : "V")}", forUsage ? typeof(bool) : typeof(object), [typeof(void*), typeof(int)], type.Module, true);
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
        return (TDelegate)dm.CreateDelegate(typeof(TDelegate));
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