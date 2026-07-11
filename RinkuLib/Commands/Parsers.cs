using System.Reflection;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.Commands;
/// <summary>
/// The columns <typeparamref name="T"/> maps to, worked out once from its shape and cached. A read-only view
/// of how the mapper sees a type, the column names, their CLR types, and which are nullable, without touching
/// a database.
/// </summary>
/// <typeparam name="T">The type whose columns to describe.</typeparam>
public static class TypeSchema<T> {
    /// <summary>
    /// The columns of <typeparamref name="T"/>, in the order the mapper reads them.
    /// </summary>
    public static ColumnInfo[] Schema => _schema;

    internal static ColumnInfo[] _schema = SchemaExtractor.FromType(typeof(T));
}

/// <summary>
/// Works out the columns a type maps to, its names, CLR types, and nullability, from a type, constructor, or
/// method. This is the shape <see cref="TypeSchema{T}"/> caches, exposed for describing a type on your own terms.
/// </summary>
public static class SchemaExtractor {
    /// <summary>The columns of <paramref name="type"/>, taken from its longest constructor when it has one, otherwise its public properties and fields.</summary>
    public static ColumnInfo[] FromType(Type type) {
        var nullabilityContext = new NullabilityInfoContext();
        var ctor = FindBestConstructor(type);
        if (ctor != null)
            return FromParameters(ctor.GetParameters(), nullabilityContext);
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance);
        var columns = new List<ColumnInfo>();
        foreach (var member in members) {
            if (member is PropertyInfo prop)
                Add(columns, prop.Name, prop.PropertyType, nullabilityContext.Create(prop));
            else if (member is FieldInfo field)
                Add(columns, field.Name, field.FieldType, nullabilityContext.Create(field));
        }
        return [.. columns];
    }
    /// <summary>The columns for a set of parameters, one per parameter, taking its bound name and nullability.</summary>
    public static ColumnInfo[] FromParameters(ParameterInfo[] parameters)
        => FromParameters(parameters, new NullabilityInfoContext());
    /// <inheritdoc cref="FromParameters(ParameterInfo[])"/>
    public static ColumnInfo[] FromParameters(ParameterInfo[] parameters, NullabilityInfoContext ctx) {
        var columns = new List<ColumnInfo>(parameters.Length);

        foreach (var param in parameters) {
            string name =
                INameComparer.TryGetTrueName(param, out var trueName)
                    ? trueName
                    : param.Name!;

            Add(columns, name, param.ParameterType, ctx.Create(param));
        }

        return [.. columns];
    }
    private static void Add(List<ColumnInfo> columns, string name, Type type, NullabilityInfo nullInfo) {
        Type? underlying = Nullable.GetUnderlyingType(type);
        bool isNullable = underlying != null || nullInfo.WriteState == NullabilityState.Nullable;
        columns.Add(new ColumnInfo(name, underlying ?? type, isNullable));
    }
    private static ConstructorInfo? FindBestConstructor(Type type) {
        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        ConstructorInfo? best = null;
        int bestScore = -1;

        foreach (var ctor in ctors) {
            var score = ctor.GetParameters().Length;
            if (score > bestScore) {
                best = ctor;
                bestScore = score;
            }
        }

        return best;
    }
    /// <summary>The columns for a method's parameters, one per parameter.</summary>
    public static ColumnInfo[] FromMethod(MethodBase method)
        => FromParameters(method.GetParameters());
    /// <summary>The columns for a constructor's parameters, one per parameter.</summary>
    public static ColumnInfo[] FromConstructor(ConstructorInfo ctor)
        => FromParameters(ctor.GetParameters());
}