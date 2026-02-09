using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly unsafe ref struct TypeAccessor(void* item, Func<IntPtr, int, bool> usage, Func<IntPtr, int, object> value) {
    // Store as IntPtr to match the delegate signature perfectly
    private readonly IntPtr _item = (IntPtr)item;
    private readonly Func<IntPtr, int, bool> _getUsage = usage;
    private readonly Func<IntPtr, int, object> _getValue = value;
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
    private static (object Key, Func<IntPtr, int, bool> Usage, Func<IntPtr, int, object> Value)[] Variants = [];
    /// <summary>
    /// Get the compiled accesor
    /// </summary>
    public static (Func<IntPtr, int, bool> Usage, Func<IntPtr, int, object> Value) GetOrGenerate(int startVariable, Mapper mapper) {
        var currentVariants = Variants;
        foreach (var (Keys, Usage, Value) in currentVariants)
            if (ReferenceEquals(Keys, mapper))
                return (Usage, Value);

        lock (SharedLock) {
            foreach (var (Keys, Usage, Value) in Variants)
                if (ReferenceEquals(Keys, mapper))
                    return (Usage, Value);

            var usage = GenerateDelegate<Func<IntPtr, int, bool>>(startVariable, mapper, true);
            var value = GenerateDelegate<Func<IntPtr, int, object>>(startVariable, mapper, false);

            Variants = [.. Variants, (mapper, usage, value)];
            return (usage, value);
        }
    }
    private static TDelegate GenerateDelegate<TDelegate>(int startVariable, Mapper mapper, bool forUsage) where TDelegate : Delegate {
        var varChar = mapper.Count > startVariable ? mapper.Keys[startVariable][0] : '\0';
        Type type = typeof(T);
        DynamicMethod dm = new($"{type.Name}_{(forUsage ? "U" : "V")}", forUsage ? typeof(bool) : typeof(object), [typeof(IntPtr), typeof(int)], true);
        ILGenerator il = dm.GetILGenerator();

        int switchCount = mapper.Count;
        AccessorEmitter?[] plans = new AccessorEmitter?[switchCount];
        Label[] switchTable = new Label[switchCount];
        Label defaultLabel = il.DefineLabel();

        for (int i = 0; i < switchCount; i++)
            switchTable[i] = defaultLabel;

        MemberInfo[] allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        int index;
        foreach (var member in allMembers) {
            // handle custom attributes
            if (member is not FieldInfo && member is not PropertyInfo)
                continue;
            index = GetIndexAppendVarChar(varChar, mapper, member);
            if (index >= 0 && index < switchCount) {
                plans[index] = forUsage
                    ? new MemberUsageEmitter(typeof(T), member)
                    : new MemberValueEmitter(typeof(T), member);
                switchTable[index] = il.DefineLabel();
            }
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