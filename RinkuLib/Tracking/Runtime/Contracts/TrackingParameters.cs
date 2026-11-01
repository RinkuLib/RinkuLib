using System.Linq.Expressions;
using System.Reflection;
using Rinku.Mapping.Conversion;

namespace Rinku.Tracking.Runtime;

/// <summary>Pending confirmation values, separate from editable and accepted values.</summary>
public interface ITrackingParameters {
    /// <summary>Gets or sets values produced by the current processing operation.</summary>
    TrackingParameters? PendingParameters { get; set; }
}

/// <summary>Allocated only when a processing operation supplies named values.</summary>
public sealed class TrackingParameters {
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Stores a pending value by name.</summary>
    public void Set<T>(string name, T value) => _values[name] = value;
    /// <summary>Attempts to read and convert a pending value by name.</summary>
    public bool TryGet<T>(string name, out T? value) {
        if (_values.TryGetValue(name, out var stored)) {
            if ((stored is null || stored is DBNull) && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
                throw new RinkuBindingException(ErrorCodes.ValueNotSet, name);
            value = stored.Parse<T>();
            return true;
        }
        value = default;
        return false;
    }
}

/// <summary>Reads constructor values from pending parameters or tracked members.</summary>
public static class TrackingConstruction {
    /// <summary>Gets a named value from pending parameters or the tracked item.</summary>
    public static T Get<T>(IRuntimeMemberAccess item, string name) {
        if (item is ITrackingParameters { PendingParameters: { } parameters } && parameters.TryGet<T>(name, out var value)) {
            if (value is null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
                throw new RinkuBindingException(ErrorCodes.ValueNotSet, name);
            return value!;
        }
        return item.Get<T>(name)!;
    }

    /// <summary>Gets a required named value while respecting the nullability policy.</summary>
    public static TValue Required<TOriginal, TValue>(ITrackingItem<TOriginal> item, string name, bool allowNull) {
        if (item.PendingParameters is { } parameters && parameters.TryGet<TValue>(name, out var value)) {
            if (value is null && !allowNull) throw new RinkuBindingException(ErrorCodes.ValueNotSet, name);
            return value!;
        }
        if (!item.TryGetOriginal(out _)) throw new RinkuBindingException(ErrorCodes.ValueNotSet, name);
        return item.Get<TValue>(name)!;
    }

    internal static Func<ITrackingItem<T>, T> Constructor<T>(ConstructorInfo? constructor) {
        if (constructor is null) {
            var constructors = typeof(T).GetConstructors();
            constructor = constructors.Length == 1 ? constructors[0] : throw new AmbiguousMatchException($"Select a constructor for {typeof(T)}.");
        }
        var item = Expression.Parameter(typeof(ITrackingItem<T>), "item");
        var parameters = constructor.GetParameters();
        var arguments = new Expression[parameters.Length];
        var nullability = new NullabilityInfoContext();
        for (int index = 0; index < parameters.Length; index++) {
            var parameter = parameters[index];
            var member = typeof(T).GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var field = typeof(T).GetField(parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            bool readOnly = member is not null ? member.SetMethod is null : field?.IsInitOnly == true;
            var method = readOnly ? typeof(TrackingConstruction).GetMethod(nameof(Required))!.MakeGenericMethod(typeof(T), parameter.ParameterType) : typeof(TrackingConstruction).GetMethod(nameof(Get))!.MakeGenericMethod(parameter.ParameterType);
            bool allowNull = Nullable.GetUnderlyingType(parameter.ParameterType) is not null || !parameter.ParameterType.IsValueType && nullability.Create(parameter).ReadState != NullabilityState.NotNull;
            arguments[index] = readOnly ? Expression.Call(method, item, Expression.Constant(parameter.Name!), Expression.Constant(allowNull)) : Expression.Call(method, item, Expression.Constant(parameter.Name!));
        }
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters) assigned.Add(parameter.Name!);
        var bindings = new List<MemberBinding>();
        Expression Read(Type type, string name) => Expression.Call(typeof(TrackingConstruction).GetMethod(nameof(Get))!.MakeGenericMethod(type), item, Expression.Constant(name));
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0 && !assigned.Contains(property.Name))
                bindings.Add(Expression.Bind(property, Read(property.PropertyType, property.Name)));
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!field.IsInitOnly && !assigned.Contains(field.Name))
                bindings.Add(Expression.Bind(field, Read(field.FieldType, field.Name)));
        return Expression.Lambda<Func<ITrackingItem<T>, T>>(Expression.MemberInit(Expression.New(constructor, arguments), bindings), item).Compile();
    }
}

