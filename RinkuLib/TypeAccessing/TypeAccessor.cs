using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// access of an item members
/// </summary>
public interface ITypeAccessor {
    /// <summary>
    /// Check if the value is used
    /// </summary>
    public bool IsUsed(int index);
    /// <summary>
    /// Get the used value
    /// </summary>
    public object GetValue(int index);
}
/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly struct NoTypeAccessor : ITypeAccessor {
    /// <inheritdoc/>
    public bool IsUsed(int index) => false;
    /// <inheritdoc/>
    public object GetValue(int index) => null!;
}
/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly ref struct TypeAccessor(object item, Func<object, int, bool> usage, Func<object, int, object> value) : ITypeAccessor {
    private readonly object _item = item;
    private readonly Func<object, int, bool> _getUsage = usage;
    private readonly Func<object, int, object> _getValue = value;
    /// <inheritdoc/>
    public bool IsUsed(int index) => _getUsage(_item, index);
    /// <inheritdoc/>
    public object GetValue(int index) => _getValue(_item, index);
}
/// <summary>
/// IL compiled access of an item
/// </summary>
public readonly ref struct TypeAccessor<T>(ref T item, MemberUsageDelegate<T> usage, MemberValueDelegate<T> value) : ITypeAccessor {
    private readonly ref T _item = ref item;
    private readonly MemberUsageDelegate<T> _getUsage = usage;
    private readonly MemberValueDelegate<T> _getValue = value;
    /// <inheritdoc/>
    public bool IsUsed(int index) => _getUsage(ref _item, index);
    /// <inheritdoc/>
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
    public TypeAccessorCache((DynamicMethod usageMethod, DynamicMethod valueMethod) methods)
        : this(methods.usageMethod, methods.valueMethod) { }
    /// <inheritdoc/>
    public TypeAccessorCache(DynamicMethod usageMethod, DynamicMethod valueMethod) {
        this.GetUsage = usageMethod.CreateDelegate<Func<object, int, bool>>(null);
        this.GetValue = valueMethod.CreateDelegate<Func<object, int, object>>(null);
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
    public StructTypeAccessorCache((DynamicMethod usageMethod, DynamicMethod valueMethod) methods)
        : this(methods.usageMethod, methods.valueMethod) {}
    /// <inheritdoc/>
    public StructTypeAccessorCache(DynamicMethod usageMethod, DynamicMethod valueMethod) {
        this.GenericGetUsage = usageMethod.CreateDelegate<MemberUsageDelegate<T>>(null);
        this.GenericGetValue = valueMethod.CreateDelegate<MemberValueDelegate<T>>(null);
        this.GetUsage = CreateBoxedWrapper<bool>(usageMethod);
        this.GetValue = CreateBoxedWrapper<object>(valueMethod);
    }

    private static Func<object, int, TReturn> CreateBoxedWrapper<TReturn>(DynamicMethod internalMethod) {
        var wrapper = new DynamicMethod($"BoxedWrapper_{internalMethod.Name}", typeof(TReturn), 
            [typeof(object), typeof(object), typeof(int)], typeof(T).Module, skipVisibility: true);
        ILGenerator il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox, typeof(T));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, internalMethod);
        il.Emit(OpCodes.Ret);

        return wrapper.CreateDelegate<Func<object, int, TReturn>>(null);
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
            TypeAccessorCache cache = typeof(T).IsValueType
                ? new StructTypeAccessorCache<T>(GenerateDelegate(mapper))
                : new TypeAccessorCache(GenerateDelegate(mapper));

            Variants = [.. Variants, (mapper, cache)];
            return cache;
        }
    }
    private static (DynamicMethod Usage, DynamicMethod Value) GenerateDelegate(Mapper mapper) {
        var varChar = mapper.Count <= 0 ? default : mapper.Keys[0].Length <= 0 ? default : mapper.Keys[0][0];
        Type type = typeof(T);
        Type arg0 = type.IsValueType ? type.MakeByRefType() : typeof(object);
        DynamicMethod usageDm = new($"{type.Name}_U", typeof(bool), [typeof(object), arg0, typeof(int)], type.Module, true);
        DynamicMethod valueDm = new($"{type.Name}_V", typeof(object), [typeof(object), arg0, typeof(int)], type.Module, true);
        var usageIl = usageDm.GetILGenerator();
        var valueIl = valueDm.GetILGenerator();

        int switchCount = mapper.Count;
        IAccessorEmiter?[] usagePlans = new IAccessorEmiter?[switchCount];
        IAccessorEmiter?[] valuePlans = new IAccessorEmiter?[switchCount];
        var typeHandlers = type.GetCustomAttributes<AccessorEmiterHandler>();
        foreach (var handler in typeHandlers)
            handler.HandleEmit(varChar, usagePlans, valuePlans, type, null, mapper);

        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        foreach (var member in members) {
            var handler = member.GetCustomAttribute<AccessorEmiterHandler>();
            if (handler is not null) {
                handler.HandleEmit(varChar, usagePlans, valuePlans, type, member, mapper);
                continue;
            }

            if (member is FieldInfo or PropertyInfo) {
                int index = mapper.GetIndex(varChar, member.Name);
                if (index >= 0) {
                    usagePlans[index] = new MemberUsageEmitter(type, member);
                    valuePlans[index] = new MemberValueEmitter(type, member);
                }
            }
        }
        HandlePlans(usagePlans, usageIl, OpCodes.Ldc_I4_0);
        HandlePlans(valuePlans, valueIl, OpCodes.Ldnull);
        return (usageDm, valueDm);
    }
    private static void HandlePlans(IAccessorEmiter?[] plans, ILGenerator il, OpCode defaultOpCode) {
        var switchCount = plans.Length;
        Label[] switchTable = new Label[switchCount];
        Label valueDefaultLabel = il.DefineLabel();
        for (int i = 0; i < switchCount; i++)
            switchTable[i] = plans[i] is null ? valueDefaultLabel : il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Switch, switchTable);

        il.MarkLabel(valueDefaultLabel);
        il.Emit(defaultOpCode);
        il.Emit(OpCodes.Ret);

        for (int i = 0; i < switchCount; i++) {
            var plan = plans[i];
            if (plan is null)
                continue;
            il.MarkLabel(switchTable[i]);
            plan.Emit(il);
            il.Emit(OpCodes.Ret);
        }
    }
}