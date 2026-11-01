using System.Reflection;

namespace Rinku.Mapping;

/// <summary>Discovers schema attributes through their <see cref="IEnumerable{T}"/> capability for <see cref="MemberInfo"/>.</summary>
public static class SchemaReference {
    /// <summary>Returns schema references from every already-loaded assembly.</summary>
    public static IEnumerable<(Type ResultType, MemberInfo Member)> GetAllSchemas() {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!assembly.IsDynamic)
                foreach ((Type resultType, MemberInfo member) schema in assembly.GetSchemas())
                    yield return schema;
        }
    }

    /// <summary>Returns every result type in <paramref name="assembly"/> with a schema reference.</summary>
    public static IEnumerable<(Type ResultType, MemberInfo Member)> GetSchemas(this Assembly assembly) => EnumerateSchemas(assembly, null);

    /// <summary>Returns the schema references declared on one result type.</summary>
    public static IEnumerable<(Type ResultType, MemberInfo Member)> GetSchemas(this Type resultType) => EnumerateSchemas(resultType.Assembly, resultType);

    private static IEnumerable<(Type ResultType, MemberInfo Member)> EnumerateSchemas(Assembly assembly, Type? requestedResultType) {
        IEnumerable<Type> types = requestedResultType is null ? GetTypes(assembly) : [requestedResultType];
        foreach (Type resultType in types) {
            if (!resultType.CustomAttributes.Any(attribute => typeof(IEnumerable<MemberInfo>).IsAssignableFrom(attribute.AttributeType)))
                continue;
            foreach (IEnumerable<MemberInfo> reference in resultType.GetCustomAttributes(false).OfType<IEnumerable<MemberInfo>>())
                foreach (MemberInfo member in reference)
                    yield return (resultType, member);
        }
    }

    private static Type[] GetTypes(Assembly assembly) {
        try {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception) {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}
