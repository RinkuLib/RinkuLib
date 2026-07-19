using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;
/// <summary>
/// Reads a parameter object one key at a time, by the mapper's index, telling whether the key is present and
/// what its value is. The uniform view the engine uses over any parameter object, whatever its shape.
/// </summary>
public interface ITypeAccessor {
    /// <summary>Whether the key at <paramref name="index"/> is present on the object.</summary>
    public bool IsUsed(int index);
    /// <summary>The value for the key at <paramref name="index"/>.</summary>
    public object GetValue(int index);
}
/// <summary>
/// The empty accessor, used for a <see langword="null"/> parameter object, every key reads as absent.
/// </summary>
public readonly struct NoTypeAccessor : ITypeAccessor {
    /// <inheritdoc/>
    public bool IsUsed(int index) => false;
    /// <inheritdoc/>
    public object GetValue(int index) => null!;
}
/// <summary>
/// An accessor bound to one object, reading its keys through the compiled readers built for its type.
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
/// The <see cref="TypeAccessor"/> variant that reads a value type by reference, so a struct parameter object
/// is read without a copy or boxing.
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
/// The compiled readers for a type against a mapper, one to test presence and one to fetch a value by index.
/// Built once per type and reused, so reading a familiar parameter object is cheap.
/// </summary>
public class TypeAccessorCache {
    /// <summary>Reads whether a key is present on the object.</summary>
    public Func<object, int, bool> GetUsage;
    /// <summary>Reads a key's value from the object.</summary>
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
/// <summary>Reads whether a key is present on an object passed by reference.</summary>
public delegate bool MemberUsageDelegate<T>(ref T instance, int index);
/// <summary>Reads a key's value from an object passed by reference.</summary>
public delegate object MemberValueDelegate<T>(ref T instance, int index);
/// <summary>
/// The <see cref="TypeAccessorCache"/> for a value type, adding by-reference readers so a struct parameter
/// object is read without boxing.
/// </summary>
public class StructTypeAccessorCache<T> : TypeAccessorCache {
    /// <summary>Reads whether a key is present, taking the struct by reference.</summary>
    public MemberUsageDelegate<T> GenericGetUsage;
    /// <summary>Reads a key's value, taking the struct by reference.</summary>
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
/// Builds and caches the compiled readers for <typeparamref name="T"/> against a mapper, once per mapper,
/// honoring the accessor attributes on the type and its members.
/// </summary>
public static class TypeAccessorCacher<T> {
    /// <summary>
    /// Guards the shared cache while the readers for a new mapper are compiled.
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
    /// The compiled readers for <typeparamref name="T"/> against <paramref name="mapper"/>, built on first
    /// request and reused after.
    /// </summary>
    /// <param name="mapper">The keys the readers are indexed by.</param>
    /// <param name="handlers">
    /// The handlers behind the mapper's handled keys, each offered the chance to fold a cheap part of its
    /// own rule into the presence check. Leaving it out puts every key on the ordinary check.
    /// </param>
    /// <param name="handlersStart">The key index <paramref name="handlers"/> starts at.</param>
    public static TypeAccessorCache GetOrGenerate(Mapper mapper, SpecialHandler[]? handlers = null, int handlersStart = 0) {
        var currentVariants = Variants;
        foreach (var (Keys, Cache) in currentVariants)
            if (ReferenceEquals(Keys, mapper))
                return Cache;

        lock (SharedLock) {
            foreach (var (Keys, Cache) in Variants)
                if (ReferenceEquals(Keys, mapper))
                    return Cache;
            TypeAccessorCache cache = typeof(T).IsValueType
                ? new StructTypeAccessorCache<T>(GenerateDelegate(mapper, handlers, handlersStart))
                : new TypeAccessorCache(GenerateDelegate(mapper, handlers, handlersStart));

            Variants = [.. Variants, (mapper, cache)];
            return cache;
        }
    }
    private static (DynamicMethod Usage, DynamicMethod Value) GenerateDelegate(Mapper mapper, SpecialHandler[]? handlers, int handlersStart) {
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
                    var handled = handlers is null ? -1 : index - handlersStart;
                    usagePlans[index] = (handled >= 0 && handled < handlers!.Length
                            ? handlers[handled].GetUsageEmitter(type, member)
                            : null)
                        ?? new MemberUsageEmitter(type, member);
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