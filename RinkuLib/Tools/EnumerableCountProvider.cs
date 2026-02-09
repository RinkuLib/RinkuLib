using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace RinkuLib.Tools;
/// <summary>
/// Interface that indicate an IEnumerable object speficif interface that provide a count
/// </summary>
public interface ICountableEnumerablePossibility {
    /// <summary>
    /// Try to get/make the delegate that will get the count if the interface match.
    /// </summary>
    public bool TryGetDelegate(Type iFace, ConcurrentDictionary<Type, Func<object, int>> cache, out Func<object, int>? func);
}
/// <summary>
/// Handles generic interface definitions like ICollection or IReadOnlyCollection
/// </summary>
public class GenericCountContract(Type genericDefinition, string propertyName = "Count") : ICountableEnumerablePossibility {
    private readonly Type _genericDefinition = genericDefinition;
    private readonly string _propertyName = propertyName;
    /// <inheritdoc/>
    public bool TryGetDelegate(Type iFace, ConcurrentDictionary<Type, Func<object, int>> cache, out Func<object, int>? func) {
        if (iFace.IsGenericType && iFace.GetGenericTypeDefinition() == _genericDefinition) {
            func = cache.GetOrAdd(iFace, t => BuildDelegate(t, _propertyName));
            return true;
        }
        func = null;
        return false;
    }

    private static Func<object, int> BuildDelegate(Type t, string propName) {
        var prop = t.GetProperty(propName)!;
        var param = Expression.Parameter(typeof(object));
        var cast = Expression.Convert(param, t);
        var body = Expression.Property(cast, prop);
        return Expression.Lambda<Func<object, int>>(body, param).Compile();
    }
}
/// <summary>
/// A class to get non enumerable count in non generic IEnumerable
/// </summary>
public static class EnumerableCountProvider {
    private static readonly ConcurrentDictionary<Type, Func<object, int>> _cache = new();

    private static readonly ICountableEnumerablePossibility[] _contracts = [
        new GenericCountContract(typeof(ICollection<>)),
        new GenericCountContract(typeof(IReadOnlyCollection<>))
    ];


    /// <summary>
    ///   Attempts to determine the number of elements in a sequence without forcing an enumeration.
    /// </summary>
    /// <param name="source">A sequence that contains elements to be counted.</param>
    /// <param name="count">
    ///     When this method returns, contains the count of <paramref name="source" /> if successful,
    ///     or zero if the method failed to determine the count.</param>
    /// <returns>
    ///   <see langword="true" /> if the count of <paramref name="source"/> can be determined without enumeration;
    ///   otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///   The method performs a series of type tests, identifying common subtypes whose
    ///   count can be determined without enumerating; this includes <see cref="ICollection{T}"/>,
    ///   <see cref="ICollection"/> as well as internal types used in the LINQ implementation.
    ///
    ///   The method is typically a constant-time operation, but ultimately this depends on the complexity
    ///   characteristics of the underlying collection implementation.
    /// </remarks>
    public static bool TryGetNonEnumeratedCount(this IEnumerable source, out int count) {
        var concreteType = source.GetType();
        if (_cache.TryGetValue(concreteType, out var getter)) {
            count = getter.Invoke(source);
            return true;
        }
        return ResolveSlow(source, concreteType, out count);
    }

    private static bool ResolveSlow(IEnumerable source, Type concreteType, out int count) {
        var interfaces = concreteType.GetInterfaces();
        Func<object, int>? func = null;
        foreach (var contract in _contracts)
            foreach (var iFace in interfaces)
                if (contract.TryGetDelegate(iFace, _cache, out func))
                    goto Out;
    Out:
        if (func is null) {
            count = default;
            return false;
        }
        _cache.TryAdd(concreteType, func);
        count = func(source);
        return true;
    }

}