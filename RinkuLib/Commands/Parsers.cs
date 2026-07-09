using System.Reflection;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.Commands;
/// <summary>
/// A registry for schema metadata extraction.
/// </summary>
/// <typeparam name="T">The type to extract schema from.</typeparam>
public static class TypeSchema<T> {
    /// <summary>
    /// The extracted schema for <typeparamref name="T"/>.
    /// </summary>
    public static ColumnInfo[] Schema => _schema;

    internal static ColumnInfo[] _schema = SchemaExtractor.FromType(typeof(T));
}

/// <summary>
/// Schema metadata extraction utilities.
/// </summary>
public static class SchemaExtractor {
    /// <summary></summary>
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
    /// <summary></summary>
    public static ColumnInfo[] FromParameters(ParameterInfo[] parameters)
        => FromParameters(parameters, new NullabilityInfoContext());
    /// <summary></summary>
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
    /// <summary></summary>
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
    /// <summary></summary>
    public static ColumnInfo[] FromMethod(MethodBase method)
        => FromParameters(method.GetParameters());
    /// <summary></summary>
    public static ColumnInfo[] FromConstructor(ConstructorInfo ctor)
        => FromParameters(ctor.GetParameters());
}