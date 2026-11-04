using System.Reflection;

namespace Rinku.More;

public static class MemberValueExtensions
{
    public static IEnumerable<(string Name, T Value)> GetStaticMemberValues<T>(this Type type, bool? isPublic = true)
    {
        BindingFlags visibility = isPublic switch { true => BindingFlags.Public, false => BindingFlags.NonPublic, null => BindingFlags.Public | BindingFlags.NonPublic };
        foreach (var field in type.GetFields(BindingFlags.Static | visibility))
        {
            if (typeof(T).IsAssignableFrom(field.FieldType) && field.GetValue(null) is T value)
            {
                yield return (field.Name, value);
            }
        }
        foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (typeof(T).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) is { } getter && (isPublic is null || getter.IsPublic == isPublic) && property.GetValue(null) is T value)
            {
                yield return (property.Name, value);
            }
        }
    }
}
