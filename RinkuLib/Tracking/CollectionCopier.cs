using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace RinkuLib.Tracking;
/// <summary>Copying a collection, either sharing its elements (<see cref="ShallowCopy"/>) or cloning each one (<see cref="DeepCopy"/>).</summary>
public static class CollectionCopyExtensions {
    private static readonly ConcurrentDictionary<Type, Func<object, object>> ShallowDispatchers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> DeepCollectionDispatchers = new();
    /// <summary>Clones the collection container while sharing element references.</summary>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? ShallowCopy<T>(this T? source) where T : IEnumerable {
        if (source is null)
            return source;

        Type declaredType = typeof(T);
        Type runtimeType = source.GetType();

        if (runtimeType == declaredType)
            return ShallowCopier<T>.Copy(source);

        return (T)ShallowDispatchers.GetOrAdd(runtimeType, CreateDispatcher(typeof(ShallowCopier<>)))(source);
    }
    /// <summary>Clones the collection container and invokes <see cref="CopyExtensions.Copy{T}"/> on each element.</summary>
    [return: NotNullIfNotNull(nameof(source))]
    public static T? DeepCopy<T>(this T? source) where T : IEnumerable {
        if (source is null)
            return source;

        Type declaredType = typeof(T);
        Type runtimeType = source.GetType();

        if (runtimeType == declaredType)
            return DeepCopier<T>.Copy(source);

        return (T)DeepCollectionDispatchers.GetOrAdd(runtimeType, CreateDispatcher(typeof(DeepCopier<>)))(source);
    }
    private static Func<Type, Func<object, object>> CreateDispatcher(Type copierGenericType) {
        return runtimeType => {
            MethodInfo copyMethod = copierGenericType
                .MakeGenericType(runtimeType)
                .GetMethod(nameof(ShallowCopier<>.Copy), BindingFlags.Public | BindingFlags.Static)!;
            ParameterExpression parameter = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, object>>(
                Expression.Convert(
                    Expression.Call(copyMethod, Expression.Convert(parameter, runtimeType)),
                    typeof(object)),
                parameter)
                .Compile();
        };
    }
    internal static void PopulateShallowCollection<TCollection, TElement>(TCollection clone, TCollection source)
        where TCollection : ICollection<TElement> {
        foreach (var item in source)
            clone.Add(item);
    }
    internal static void PopulateShallowList(IList clone, IList source) {
        foreach (var item in source)
            clone.Add(item);
    }
    internal static void PopulateDeepCollection<TCollection, TElement>(TCollection clone, TCollection source)
        where TCollection : IEnumerable<TElement>, ICollection<TElement> {
        foreach (var item in source)
            clone.Add(item.Copy()!);
    }
    internal static void PopulateDeepDictionary<TDict, TKey, TValue>(TDict clone, TDict source)
        where TDict : IDictionary<TKey, TValue> {
        foreach (KeyValuePair<TKey, TValue> entry in source)
            clone.Add(
                entry.Key == null ? default! : entry.Key.Copy(),
                entry.Value == null ? default! : entry.Value.Copy()
            );
    }
    internal static void PopulateDeepList(IList clone, IList source) {
        foreach (object? item in source)
            clone.Add(item?.Copy());
    }
    internal static void PopulateDeepNonGenericDictionary(IDictionary clone, IDictionary source) {
        foreach (DictionaryEntry entry in source)
            clone.Add(
                entry.Key == null ? default! : entry.Key.Copy(),
                entry.Value == null ? default! : entry.Value.Copy()
            );
    }
}
internal static class ShallowCopier<T> where T : IEnumerable {
    private static readonly Func<T, T> _strategy = Build();
    internal static T Copy(T source) => _strategy(source);
    private static Func<T, T> Build() {
        Type type = typeof(T);
        if (type == typeof(string))
            return source => source;

        if (type.IsAbstract || type.IsInterface)
            throw new RinkuTrackingException(ErrorCodes.NoCopyStrategy, $"Cannot directly instantiate abstract type or interface {type}. The dispatcher handles this.");

        ParameterExpression sourceParam = Expression.Parameter(type, "source");
        if (type.IsArray) {
            MethodInfo cloneMethod = typeof(Array).GetMethod(nameof(Array.Clone))!;
            Expression arrayCast = Expression.Convert(sourceParam, typeof(Array));
            Expression callClone = Expression.Call(arrayCast, cloneMethod);
            Expression resultCast = Expression.Convert(callClone, type);
            return Expression.Lambda<Func<T, T>>(resultCast, sourceParam).Compile();
        }
        ConstructorInfo? copyCtor = GetEnumerableConstructor(type);
        if (copyCtor != null) {
            Type paramType = copyCtor.GetParameters()[0].ParameterType;
            Expression body = Expression.New(copyCtor, Expression.Convert(sourceParam, paramType));
            return Expression.Lambda<Func<T, T>>(body, sourceParam).Compile();
        }
        ConstructorInfo? emptyCtor = type.GetConstructor(Type.EmptyTypes);
        if (emptyCtor != null) {
            Type? genericEnumerable = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (genericEnumerable != null) {
                Type elementType = genericEnumerable.GetGenericArguments()[0];
                Type collectionType = typeof(ICollection<>).MakeGenericType(elementType);

                if (collectionType.IsAssignableFrom(type)) {
                    MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateShallowCollection), BindingFlags.Public | BindingFlags.Static)!
                        .MakeGenericMethod(type, elementType);
                    return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
                }
            }
            if (typeof(IList).IsAssignableFrom(type)) {
                MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateShallowList), BindingFlags.Public | BindingFlags.Static)!;
                return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
            }
        }
        throw new RinkuTrackingException(ErrorCodes.NoCopyStrategy, $"Cannot create shallow copy strategy for {type}. No valid constructor found");
    }
    private static ConstructorInfo? GetEnumerableConstructor(Type type) {
        return type.GetConstructors().FirstOrDefault(c => {
            ParameterInfo[] p = c.GetParameters();
            if (p.Length != 1)
                return false;

            Type pt = p[0].ParameterType;
            return pt == typeof(IEnumerable) ||
                   (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(IEnumerable<>)) ||
                   (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        });
    }
    private static Func<T, T> CompileCollectionHelper(ConstructorInfo emptyCtor, MethodInfo helperMethod, Type type, ParameterExpression sourceParam) {
        ParameterExpression cloneVar = Expression.Variable(type, "clone");
        Expression assign = Expression.Assign(cloneVar, Expression.New(emptyCtor));
        Expression callHelper = Expression.Call(helperMethod, cloneVar, sourceParam);
        Expression block = Expression.Block([cloneVar], assign, callHelper, cloneVar);
        return Expression.Lambda<Func<T, T>>(block, sourceParam).Compile();
    }
}
internal static class DeepCopier<T> where T : IEnumerable {
    private static readonly Func<T, T> _strategy = Build();
    internal static T Copy(T source) => _strategy(source);
    private static Func<T, T> Build() {
        Type type = typeof(T);
        if (type == typeof(string))
            return source => source;

        if (type.IsAbstract || type.IsInterface)
            throw new RinkuTrackingException(ErrorCodes.NoCopyStrategy, $"Cannot directly instantiate abstract type or interface {type}. The dispatcher handles this.");

        ParameterExpression sourceParam = Expression.Parameter(type, "source");
        if (type.IsArray) {
            if (type.GetArrayRank() > 1)
                throw new RinkuTrackingException(ErrorCodes.NoCopyStrategy, $"Multi-dimensional array {type} is not supported");

            Type elementType = type.GetElementType()!;
            MethodInfo deepCloneMethod = typeof(DeepCopier<T>).GetMethod(nameof(DeepCloneArray), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(elementType);
            Expression call = Expression.Call(deepCloneMethod, sourceParam);
            return Expression.Lambda<Func<T, T>>(call, sourceParam).Compile();
        }
        ConstructorInfo? emptyCtor = type.GetConstructor(Type.EmptyTypes);
        if (emptyCtor != null) {
            Type? genericDict = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            if (genericDict != null) {
                Type[] args = genericDict.GetGenericArguments();
                MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateDeepDictionary), BindingFlags.Public | BindingFlags.Static)!
                    .MakeGenericMethod(type, args[0], args[1]);
                return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
            }
            Type? genericEnumerable = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (genericEnumerable != null) {
                Type elementType = genericEnumerable.GetGenericArguments()[0];
                Type collectionType = typeof(ICollection<>).MakeGenericType(elementType);
                if (collectionType.IsAssignableFrom(type)) {
                    MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateDeepCollection), BindingFlags.Public | BindingFlags.Static)!
                        .MakeGenericMethod(type, elementType);
                    return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
                }
            }
            if (typeof(IDictionary).IsAssignableFrom(type)) {
                MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateDeepNonGenericDictionary), BindingFlags.Public | BindingFlags.Static)!;
                return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
            }
            if (typeof(IList).IsAssignableFrom(type)) {
                MethodInfo helper = typeof(CollectionCopyExtensions).GetMethod(nameof(CollectionCopyExtensions.PopulateDeepList), BindingFlags.Public | BindingFlags.Static)!;
                return CompileCollectionHelper(emptyCtor, helper, type, sourceParam);
            }
        }
        throw new RinkuTrackingException(ErrorCodes.NoCopyStrategy, $"Cannot create deep copy strategy for {type}. Needs a parameterless constructor and an implementation of ICollection<T>, IList, or IDictionary.");
    }
    private static Func<T, T> CompileCollectionHelper(ConstructorInfo emptyCtor, MethodInfo helperMethod, Type type, ParameterExpression sourceParam) {
        ParameterExpression cloneVar = Expression.Variable(type, "clone");
        Expression assign = Expression.Assign(cloneVar, Expression.New(emptyCtor));
        Expression callHelper = Expression.Call(helperMethod, cloneVar, sourceParam);
        Expression block = Expression.Block([cloneVar], assign, callHelper, cloneVar);
        return Expression.Lambda<Func<T, T>>(block, sourceParam).Compile();
    }
    private static TElement[] DeepCloneArray<TElement>(TElement[] source) {
        var clone = new TElement[source.Length];
        for (int i = 0; i < source.Length; i++)
            clone[i] = source[i].Copy()!;

        return clone;
    }
}