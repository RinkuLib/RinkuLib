using System.Reflection;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeTrackingMetadataSources
{
    internal static void AddOriginal<TOriginal>(RuntimeTrackingMemberDefinition<TOriginal> member, MemberInfo source)
    {
        if (source is not PropertyInfo property)
        {
            member.AddMetadataSource(source);
            return;
        }

        var chain = new Stack<PropertyInfo>();
        PropertyInfo? current = property;
        while (current is not null)
        {
            chain.Push(current);
            Type? baseType = current.DeclaringType?.BaseType;
            current = baseType?.GetProperty(current.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                current.PropertyType,
                Type.EmptyTypes,
                null);
        }

        while (chain.Count > 1) member.AddMetadataSource(chain.Pop(), inheritedOnly: true);
        if (chain.Count == 1) member.AddMetadataSource(chain.Pop());
    }
}

internal static class RuntimeTrackingAttributeApplication
{
    internal static void Apply<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, RuntimeTrackingMemberDefinition<TOriginal> member, MemberInfo source, bool inheritedOnly = false)
    {
        object[] attributes = source.GetCustomAttributes(inherit: false);
        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i] is not IRuntimeTrackingConfigurationAttribute configuration) continue;
            AttributeUsageAttribute usage = attributes[i].GetType().GetCustomAttribute<AttributeUsageAttribute>() ?? new AttributeUsageAttribute(AttributeTargets.All);
            if (inheritedOnly && !usage.Inherited) continue;
            configuration.Apply(RuntimeTrackingMemberConfigurator.Create(type, member));
        }
    }
}
