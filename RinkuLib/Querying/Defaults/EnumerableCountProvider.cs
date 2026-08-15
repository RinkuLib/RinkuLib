using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Rinku.Querying.Defaults;
internal interface ICountableEnumerablePossibility {
    public bool TryGetDelegate(Type iFace, ConcurrentDictionary<Type, Func<object, int>> cache, out Func<object, int>? func);
}
internal class GenericCountContract(Type genericDefinition, string propertyName = "Count") : ICountableEnumerablePossibility {
    private readonly Type _genericDefinition = genericDefinition;
    private readonly string _propertyName = propertyName;
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
internal static class EnumerableCountProvider {
    private static readonly ConcurrentDictionary<Type, Func<object, int>> _cache = new();

    private static readonly ICountableEnumerablePossibility[] _contracts = [
        new GenericCountContract(typeof(ICollection<>)),
        new GenericCountContract(typeof(IReadOnlyCollection<>))
    ];


    internal static bool TryGetNonEnumeratedCount(this IEnumerable source, out int count) {
        var concreteType = source.GetType();
        if (_cache.TryGetValue(concreteType, out var getter)) {
            count = getter(source);
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
