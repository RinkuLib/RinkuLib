using System.Collections;

namespace Rinku.More;

public static class MultiValueSelection
{
    public static List<object?> Box(IEnumerable values)
    {
        List<object?> result = [];
        foreach (object? value in values)
        {
            result.Add(value);
        }
        return result;
    }

    public static bool Contains(object? values, object? value)
    {
        if (values is IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (Equals(item, value))
                {
                    return true;
                }
            }
        }
        return false;
    }
    public static object Create(Type type, IEnumerable values)
    {
        Type element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments().Single();
        if (type.IsArray && values is ICollection collection)
        {
            var result = Array.CreateInstance(element, collection.Count);
            int index = 0;
            foreach (object? value in values)
            {
                result.SetValue(value, index++);
            }
            return result;
        }
        var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        foreach (object? value in values)
        {
            items.Add(value);
        }
        if (!type.IsArray)
        {
            return items;
        }
        var array = Array.CreateInstance(element, items.Count);
        items.CopyTo(array, 0);
        return array;
    }
}
