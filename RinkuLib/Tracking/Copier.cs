using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking;
/// <summary>
/// Defines the signature for custom cloning logic.
/// </summary>
/// <typeparam name="T">The type of the object.</typeparam>
[return: NotNullIfNotNull(nameof(source))]
public delegate T? CopyDelegate<T>(T? source);
/// <summary>
/// Defines a contract for types that provide custom cloning logic.
/// </summary>
/// <typeparam name="T">The type of the returned object.</typeparam>
public interface ICopyable<T>
{
    /// <summary>
    /// Returns a new instance that is a functional copy of the current object.
    /// </summary>
    /// <remarks>
    /// Implementing this interface allows a type to override default cloning 
    /// behavior, ensuring that complex state or dependencies are handled 
    /// correctly during the copy process.
    /// </remarks>
    T Copy();
}
/// <summary>Core contract for a field copy plan emitted into the clone method.</summary>
public interface ICopyFieldPlan
{
    /// <summary>Emits the copy for <paramref name="field"/>.</summary>
    void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone);
}
/// <summary>Base attribute for a built-in or custom field copy plan.</summary>
[AttributeUsage(AttributeTargets.Field)]
public abstract class CopyFieldAttribute : Attribute, ICopyFieldPlan
{
    /// <summary>
    /// Emits IL instructions to copy the field value to the target clone.
    /// </summary>
    /// <param name="field">The metadata of the field currently being processed.</param>
    /// <param name="il">The IL generator for the target dynamic method.</param>
    /// <param name="clone">The local variable representing the new instance.</param>
    public abstract void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone);
}
/// <summary>The entry point for copying, <c>source.Copy()</c>, which clones by the strategy registered for the value's actual type.</summary>
public static class CopyExtensions {
    private static readonly ConcurrentDictionary<Type, Func<object, object?>> Dispatchers = new();
    /// <summary>
    /// Creates a copy of the specified object.
    /// </summary>
    /// <typeparam name="T">The type of the object to clone.</typeparam>
    /// <param name="source">The object instance to clone.</param>
    /// <returns>
    /// A new instance of <typeparamref name="T"/>. The specific cloning logic 
    /// (e.g., shallow, deep, or selective) is determined by the implementation 
    /// of the type <typeparamref name="T"/> and its associated field attributes.
    /// </returns>
    /// <remarks>
    /// Use this method to request a logically independent clone of an object. 
    /// The library automatically selects the most efficient strategy to fulfill 
    /// the request based on the object's structure.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? Copy<T>(this T? source) {
        if (source is null)
            return source;

        if (typeof(T).IsValueType || typeof(T).IsSealed)
            return Copier<T>.Copy(source)!;

        Type runtimeType = source.GetType();
        if (runtimeType == typeof(T))
            return Copier<T>.Copy(source)!;

        return (T)Dispatchers.GetOrAdd(runtimeType, CreateDispatcher)(source)!;
    }
    private static Func<object, object?> CreateDispatcher(Type runtimeType) {
        var copyMethod = typeof(Copier<>)
                .MakeGenericType(runtimeType)
                .GetMethod(nameof(Copier<>.Copy), BindingFlags.Public | BindingFlags.Static)!;
        ParameterExpression parameter = Expression.Parameter(typeof(object));
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(
                Expression.Call(copyMethod, Expression.Convert(parameter, runtimeType)),
                typeof(object)),
            parameter)
            .Compile();
    }
}
/// <summary>
/// Provides a centralized manager for the cloning strategy of a specific type.
/// </summary>
/// <typeparam name="T">The type being managed.</typeparam>
public static class Copier<T> {
    private static readonly CopyDelegate<T> _defaultStrategy = Build();
    private static CopyDelegate<T> _strategy = _defaultStrategy;
    private static readonly object ConfigurationLock = new();
    private static Dictionary<FieldInfo, ICopyFieldPlan> FieldPlans = [];
    /// <summary>
    /// Executes the current cloning strategy for the type.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? Copy(T? source) => _strategy(source);
    /// <summary>
    /// Executes the original, automatically detected cloning strategy for the type.
    /// </summary>
    /// <param name="source">The object to clone.</param>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? DefaultCopy(T? source) => _defaultStrategy(source);
    /// <summary>
    /// Registers a custom cloning strategy for the type.
    /// </summary>
    /// <param name="customStrategy">The delegate to perform the copy operation.</param>
    /// <remarks>
    /// This method is intended for types where you cannot modify the source code to 
    /// implement <see cref="ICopyable{T}"/> or add <see cref="CopyFieldAttribute"/> 
    /// decorations (e.g., third-party or library types). This strategy becomes the 
    /// default for all subsequent <see cref="CopyExtensions.Copy{T}"/> calls.
    /// </remarks>
    public static void SetStrategy(CopyDelegate<T> customStrategy) => _strategy = customStrategy;
    /// <summary>
    /// Reverts the cloning strategy to the original, automatically detected implementation.
    /// </summary>
    public static void ResetStrategy() => _strategy = Build(FieldPlans);
    /// <summary>
    /// Registers or replaces the plan for one field in the automatic clone strategy.
    /// </summary>
    /// <param name="field">A field declared by <typeparamref name="T"/> or one of its base types.</param>
    /// <param name="plan">The field copy plan.</param>
    /// <remarks>Register during setup. The next automatic copy uses the rebuilt strategy.</remarks>
    public static void RegisterFieldPlan(FieldInfo field, ICopyFieldPlan plan) {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(plan);
        if (field.IsStatic || field.IsLiteral)
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Field '{field}' cannot be copied.");
        if (!field.DeclaringType!.IsAssignableFrom(typeof(T)))
            throw new RinkuConfigurationException(ErrorCodes.UnusableMember, $"Field '{field}' is not declared by '{typeof(T)}' or one of its base types.");
        lock (ConfigurationLock) {
            FieldPlans = new(FieldPlans) { [field] = plan };
            _strategy = Build(FieldPlans);
        }
    }
    /// <summary>Removes all runtime field plans and restores the attribute-only automatic strategy.</summary>
    public static void ResetFieldPlans() {
        lock (ConfigurationLock) {
            FieldPlans = [];
            _strategy = _defaultStrategy;
        }
    }
    private static CopyDelegate<T> Build(IReadOnlyDictionary<FieldInfo, ICopyFieldPlan>? fieldPlans = null) {
        Type type = typeof(T);
        if (typeof(ICopyable<T>).IsAssignableFrom(type))
            return BuildCopyableStrategy(type);
        return BuildCloneStrategy(type, fieldPlans);
    }
    private static CopyDelegate<T> BuildCopyableStrategy(Type type) {
        MethodInfo copyMethod = type.GetMethod(nameof(ICopyable<>.Copy), BindingFlags.Instance | BindingFlags.Public)!
            ?? throw new RinkuTrackingException(ErrorCodes.CopyMethodNotUsable, $"{type} implements ICopyable<{type.Name}> but no Copy method was found.");
        DynamicMethod dm = new("Copyable_" + type.Name, type, [type], type.Module, true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (type.IsValueType)
            il.Emit(OpCodes.Call, copyMethod);
        else
            il.Emit(OpCodes.Callvirt, copyMethod);

        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<CopyDelegate<T>>();
    }
    private static CopyDelegate<T> BuildCloneStrategy(Type type, IReadOnlyDictionary<FieldInfo, ICopyFieldPlan>? fieldPlans) {
        DynamicMethod dm = new("Clone_" + type.Name, type, [type], type.Module, true);
        ILGenerator il = dm.GetILGenerator();
        bool isStruct = type.IsValueType;
        LocalBuilder clone = il.DeclareLocal(type);
        if (isStruct) {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, clone);
        }
        else {
            MethodInfo memberwiseClone = typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "Unable to locate object.MemberwiseClone");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, memberwiseClone);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, clone);
        }
        PatchFields(type, il, clone, fieldPlans);
        il.Emit(OpCodes.Ldloc, clone);
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<CopyDelegate<T>>();
    }
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static void PatchFields(Type type, ILGenerator il, LocalBuilder clone, IReadOnlyDictionary<FieldInfo, ICopyFieldPlan>? fieldPlans) {
        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(Flags))
                (fieldPlans is not null && fieldPlans.TryGetValue(field, out var runtimePlan)
                    ? runtimePlan
                    : field.GetCustomAttributes(false).OfType<ICopyFieldPlan>().FirstOrDefault())?.Emit(field, il, clone);
    }
}
