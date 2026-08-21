using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeCustomAttributeCopy
{
    internal static void Apply(PropertyBuilder property, IReadOnlyList<RuntimeTrackingMetadataSource> sources)
    {
        var selected = new List<CustomAttributeData>();
        var single = new Dictionary<Type, int>();

        for (int s = 0; s < sources.Count; s++)
        {
            RuntimeTrackingMetadataSource source = sources[s];
            IList<CustomAttributeData> attributes = CustomAttributeData.GetCustomAttributes(source.Source);
            for (int i = 0; i < attributes.Count; i++)
            {
                CustomAttributeData data = attributes[i];
                Type attributeType = data.AttributeType;
                if (typeof(IRuntimeTrackingConfigurationAttribute).IsAssignableFrom(attributeType)) continue;

                AttributeUsageAttribute usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>() ?? new AttributeUsageAttribute(AttributeTargets.All);
                if ((usage.ValidOn & AttributeTargets.Property) == 0) continue;
                if (source.InheritedOnly && !usage.Inherited) continue;

                if (usage.AllowMultiple)
                {
                    selected.Add(data);
                }
                else if (single.TryGetValue(attributeType, out int index))
                {
                    selected[index] = data;
                }
                else
                {
                    single.Add(attributeType, selected.Count);
                    selected.Add(data);
                }
            }
        }

        for (int i = 0; i < selected.Count; i++)
        {
            try
            {
                property.SetCustomAttribute(Create(selected[i]));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Unable to copy attribute {selected[i].AttributeType} onto generated property {property.Name}.", exception);
            }
        }
    }

    private static CustomAttributeBuilder Create(CustomAttributeData data)
    {
        object?[] constructorArguments = new object?[data.ConstructorArguments.Count];
        for (int i = 0; i < constructorArguments.Length; i++) constructorArguments[i] = Convert(data.ConstructorArguments[i]);

        var properties = new List<PropertyInfo>();
        var propertyValues = new List<object?>();
        var fields = new List<FieldInfo>();
        var fieldValues = new List<object?>();
        for (int i = 0; i < data.NamedArguments.Count; i++)
        {
            CustomAttributeNamedArgument argument = data.NamedArguments[i];
            object? value = Convert(argument.TypedValue);
            if (argument.IsField)
            {
                fields.Add((FieldInfo)argument.MemberInfo);
                fieldValues.Add(value);
            }
            else
            {
                properties.Add((PropertyInfo)argument.MemberInfo);
                propertyValues.Add(value);
            }
        }

        return new CustomAttributeBuilder(
            data.Constructor,
            constructorArguments,
            properties.ToArray(),
            propertyValues.ToArray(),
            fields.ToArray(),
            fieldValues.ToArray());
    }

    private static object? Convert(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is null) return null;
        if (argument.ArgumentType.IsArray && argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            Type elementType = argument.ArgumentType.GetElementType()
                ?? throw new InvalidOperationException($"Array attribute type {argument.ArgumentType} has no element type.");
            Array result = Array.CreateInstance(elementType, values.Count);
            int index = 0;
            foreach (CustomAttributeTypedArgument value in values) result.SetValue(Convert(value), index++);
            return result;
        }
        if (argument.ArgumentType.IsEnum) return Enum.ToObject(argument.ArgumentType, argument.Value);
        return argument.Value;
    }
}
