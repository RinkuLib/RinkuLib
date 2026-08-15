using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking;
/// <summary>Copies a value using application supplied rules.</summary>
/// <typeparam name="T">The type of the object.</typeparam>
[return: NotNullIfNotNull(nameof(source))]
public delegate T? CopyDelegate<T>(T? source);
/// <summary>Implement this interface when a type should control how it is copied.</summary>
/// <typeparam name="T">The type of the returned object.</typeparam>
public interface ICopyable<T>
{
    /// <summary>
    /// Returns a new instance that is a functional copy of the current object.
    /// </summary>
    T Copy();
}
/// <summary>Writes the copy instructions for one field. Use it for a custom field copy rule.</summary>
public interface ICopyFieldPlan
{
    /// <summary>Emits the copy for <paramref name="field"/>.</summary>
    void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone);
}
/// <summary>Base attribute for a built-in or custom field copy plan.</summary>
[AttributeUsage(AttributeTargets.Field)]
public abstract class CopyFieldAttribute : Attribute, ICopyFieldPlan
{
    /// <summary>Writes the instructions that assign the field on the copied value.</summary>
    /// <param name="field">The field to copy.</param>
    /// <param name="il">The writer to use.</param>
    /// <param name="clone">The copied value being filled.</param>
    public abstract void Emit(FieldInfo field, ILGenerator il, LocalBuilder clone);
}
/// <summary>Provides <c>source.Copy()</c> using the copy rules registered for the value type.</summary>
public static class CopyExtensions {
    private static readonly ConcurrentDictionary<Type, Func<object, object?>> Dispatchers = new();
    /// <summary>
    /// Creates a copy of the specified object.
    /// </summary>
    /// <typeparam name="T">The type of the object to clone.</typeparam>
    /// <param name="source">The object instance to clone.</param>
    /// <returns>
    /// A new instance of <typeparamref name="T"/> created with its registered copy rules.
    /// </returns>
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
/// <summary>Configures and runs copying for <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The type being managed.</typeparam>
public static class Copier<T> {
    private static readonly CopyDelegate<T> _defaultStrategy = Build();
    private static CopyDelegate<T> _strategy = _defaultStrategy;
    private static readonly object ConfigurationLock = new();
    private static Dictionary<FieldInfo, ICopyFieldPlan> FieldPlans = [];
    /// <summary>
    /// Copies the value using the current rules.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? Copy(T? source) => _strategy(source);
    /// <summary>
    /// Copies the value using the rules found on the type.
    /// </summary>
    /// <param name="source">The object to clone.</param>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? DefaultCopy(T? source) => _defaultStrategy(source);
    /// <summary>
    /// Replaces copying for this type with the supplied delegate.
    /// </summary>
    /// <param name="customStrategy">The delegate to perform the copy operation.</param>
    /// <remarks>
    /// Use this for a type you cannot change to implement <see cref="ICopyable{T}"/> or mark with
    /// <see cref="CopyFieldAttribute"/>. Later <see cref="CopyExtensions.Copy{T}"/> calls use this delegate.
    /// </remarks>
    public static void SetStrategy(CopyDelegate<T> customStrategy) => _strategy = customStrategy;
    /// <summary>
    /// Restores the copy rules found on the type and its fields.
    /// </summary>
    public static void ResetStrategy() => _strategy = Build(FieldPlans);
    /// <summary>
    /// Registers or replaces the copy rule for one field.
    /// </summary>
    /// <param name="field">A field declared by <typeparamref name="T"/> or one of its base types.</param>
    /// <param name="plan">The field copy plan.</param>
    /// <remarks>Register during setup. The next copy uses the new rule.</remarks>
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
    /// <summary>Removes registered field rules and restores the rules declared by attributes.</summary>
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
