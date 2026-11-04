using System.Reflection;

namespace Rinku.More;

public static class AttributeMetadataExtensions
{
    /// <summary>Enumerates attributes on a declaration, including matching declarations inherited through interfaces.</summary>
    public static IEnumerable<TAttribute> GetMetadataAttributes<TAttribute>(this MemberInfo member) where TAttribute : Attribute
    {
        Type? type = member as Type ?? member.DeclaringType;
        if (type?.IsInterface == true)
        {
            foreach (Type parent in type.GetInterfaces().OrderBy(parent => parent.GetInterfaces().Length))
            {
                MemberInfo? declaration = member is Type ? parent : parent.GetProperty(member.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (declaration is not null)
                {
                    foreach (TAttribute attribute in declaration.GetCustomAttributes<TAttribute>(false))
                    {
                        yield return attribute;
                    }
                }
            }
        }
        foreach (TAttribute attribute in member.GetCustomAttributes<TAttribute>(true))
        {
            yield return attribute;
        }
    }
}
