using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal static class CustomAttributeCopy {
    public static bool TryCreate(CustomAttributeData data, out CustomAttributeBuilder? builder) {
        try {
            object?[] ctorArgs = data.ConstructorArguments.Select(static x => Unwrap(x)).ToArray();
            var properties = new List<PropertyInfo>();
            var propertyValues = new List<object?>();
            var fields = new List<FieldInfo>();
            var fieldValues = new List<object?>();

            foreach (CustomAttributeNamedArgument arg in data.NamedArguments) {
                if (arg.IsField) {
                    fields.Add((FieldInfo)arg.MemberInfo);
                    fieldValues.Add(Unwrap(arg.TypedValue));
                }
                else {
                    properties.Add((PropertyInfo)arg.MemberInfo);
                    propertyValues.Add(Unwrap(arg.TypedValue));
                }
            }

            builder = new CustomAttributeBuilder(data.Constructor, ctorArgs!, properties.ToArray(), propertyValues.ToArray()!, fields.ToArray(), fieldValues.ToArray()!);
            return true;
        }
        catch {
            builder = null;
            return false;
        }
    }

    private static object? Unwrap(CustomAttributeTypedArgument arg) {
        if (arg.Value is not IReadOnlyCollection<CustomAttributeTypedArgument> values) return arg.Value;
        Array array = Array.CreateInstance(arg.ArgumentType.GetElementType()!, values.Count);
        int i = 0;
        foreach (CustomAttributeTypedArgument value in values) array.SetValue(Unwrap(value), i++);
        return array;
    }
}
